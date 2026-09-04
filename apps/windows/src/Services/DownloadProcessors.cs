using Anibel.App.Core;
using Anibel.App.Playback;

namespace Anibel.App.Services;

/// <summary>
/// Everything one download needs. The queue owner (DownloadService) resolves
/// item/status; the processors below do the network work and mutate the item
/// only through <see cref="Post"/> so the UI thread is never touched.
/// </summary>
public sealed record DownloadContext(
    ICoreClient Core,
    HttpClient Http,
    HlsDownloader Hls,
    DownloadItem Item,
    CancellationToken Ct,
    Action<Action> Post);

/// <summary>
/// Network work for a single download: episode resolution, HLS fetching,
/// manga pages, plain files, subtitles and fonts. UI mutation goes through
/// <see cref="DownloadContext.Post"/>.
/// </summary>
public static class DownloadProcessors
{
    public static async Task ProcessAsync(DownloadContext ctx)
    {
        Directory.CreateDirectory(ctx.Item.Folder);
        switch (ctx.Item.Kind)
        {
            case DownloadKind.Manga:
                await ProcessMangaAsync(ctx).ConfigureAwait(false);
                break;
            case DownloadKind.File:
                await ProcessFileAsync(ctx).ConfigureAwait(false);
                break;
            default:
                await ProcessEpisodeAsync(ctx).ConfigureAwait(false);
                break;
        }
    }

    public static async Task ProcessEpisodeAsync(DownloadContext ctx)
    {
        var item = ctx.Item;
        var ct = ctx.Ct;
        if (string.IsNullOrWhiteSpace(item.EpisodeUrl))
        {
            throw new InvalidOperationException("Няма URL эпізода.");
        }
        if (item.EpisodeUrl.Contains("drive.google", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google Drive нельга спампаваць. Абярыце крыніцу Anibel.");
        }

        var intent = await ctx.Core.ResolveEpisodeAsync(item.EpisodeUrl, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (string.Equals(intent.Kind, "embed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Гэтую крыніцу нельга спампаваць (Google Drive).");
        }

        if (item.Kind == DownloadKind.Audio)
        {
            var audio = intent.AudioSrc;
            if (string.IsNullOrWhiteSpace(audio))
            {
                var video = intent.VideoSrc;
                if (!string.IsNullOrWhiteSpace(video) && HlsSource(video))
                {
                    throw new InvalidOperationException(
                        "Аўдыё-спампаванне недаступнае для гэтай крыніцы: відэа-паток HLS без асобнай аўдыё-дарожкі.");
                }
                audio = video;
            }
            if (string.IsNullOrWhiteSpace(audio))
            {
                throw new InvalidOperationException("Няма аўдыё-патоку.");
            }
            var path = Path.Combine(item.Folder, "audio" + DownloadNames.GuessExt(audio, ".m4a"));
            await DownloadUrlAsync(ctx, audio, path).ConfigureAwait(false);
            ctx.Post(() => item.AudioPath = path);
        }
        else
        {
            var src = intent.VideoSrc
                ?? throw new InvalidOperationException("Няма відэа-патоку.");
            if (src.Contains(".mpd", StringComparison.OrdinalIgnoreCase)
                && !src.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("DASH-паток нельга спампаваць.");
            }
            var playable = await DownloadPlayableAsync(ctx, src).ConfigureAwait(false);
            ctx.Post(() => item.VideoPath = playable);
            if (intent.AudioSrc is { Length: > 0 } audioSrc)
            {
                var audioPath = Path.Combine(item.Folder, "audio" + DownloadNames.GuessExt(audioSrc, ".m4a"));
                await DownloadUrlAsync(ctx, audioSrc, audioPath).ConfigureAwait(false);
                ctx.Post(() => item.AudioPath = audioPath);
            }
        }

        var subs = new List<string>();
        var i = 0;
        foreach (var sub in intent.Subtitles ?? [])
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(item.Folder, SubtitlePicker.SafeFileName(sub.Url, sub.Label, i++));
            try
            {
                await DownloadUrlAsync(ctx, sub.Url, path).ConfigureAwait(false);
                subs.Add(path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Diag.Log($"sub download: {ex.Message}");
            }
        }
        ctx.Post(() => item.SubtitlePaths = [.. subs]);

        var fonts = new List<string>();
        var fontsDir = Path.Combine(item.Folder, "fonts");
        Directory.CreateDirectory(fontsDir);
        foreach (var font in intent.Fonts ?? [])
        {
            ct.ThrowIfCancellationRequested();
            var name = DownloadNames.Sanitize(font.Family) + ".ttf";
            var path = Path.Combine(fontsDir, name);
            try
            {
                await DownloadUrlAsync(ctx, font.Url, path).ConfigureAwait(false);
                fonts.Add(path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Diag.Log($"font download: {ex.Message}");
            }
        }
        ctx.Post(() => item.FontPaths = [.. fonts]);
    }

    public static async Task ProcessMangaAsync(DownloadContext ctx)
    {
        var item = ctx.Item;
        var ct = ctx.Ct;
        if (item.Chapter is null)
        {
            throw new InvalidOperationException("Няма нумара главы.");
        }
        var chapter = await ctx.Core.ChapterAsync(item.Slug, item.Chapter.Value, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (chapter is null || chapter.Images.Length == 0)
        {
            throw new InvalidOperationException("Няма старонак для гэтай главы.");
        }
        if (!string.IsNullOrEmpty(chapter.Id))
        {
            item.ChapterId = chapter.Id;
        }
        if (string.IsNullOrWhiteSpace(item.Subtitle) && !string.IsNullOrWhiteSpace(chapter.Title))
        {
            ctx.Post(() => item.Subtitle = chapter.Title);
        }

        var pagesDir = Path.Combine(item.Folder, "pages");
        Directory.CreateDirectory(pagesDir);
        var paths = new string[chapter.Images.Length];
        ctx.Post(() =>
        {
            item.PartsTotal = chapter.Images.Length;
            item.PartsDone = 0;
        });
        for (var i = 0; i < chapter.Images.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var url = chapter.Images[i].Large;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = chapter.Images[i].Thumbnail ?? "";
            }
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }
            var ext = DownloadNames.GuessExt(url, ".jpg");
            var path = Path.Combine(pagesDir, $"{i + 1:000}{ext}");
            await DownloadUrlAsync(ctx, url, path).ConfigureAwait(false);
            paths[i] = new Uri(path).AbsoluteUri;
            var done = i + 1;
            ctx.Post(() =>
            {
                item.PartsDone = done;
                item.Progress = done / (double)chapter.Images.Length;
            });
        }
        ctx.Post(() => item.ImagePaths = paths.Where(p => p is { Length: > 0 }).ToArray());
    }

    public static async Task ProcessFileAsync(DownloadContext ctx)
    {
        var item = ctx.Item;
        var ct = ctx.Ct;
        if (string.IsNullOrWhiteSpace(item.FileUrl))
        {
            throw new InvalidOperationException("Няма URL файла.");
        }
        var ext = DownloadNames.GuessExt(item.FileUrl, "");
        var path = Path.Combine(item.Folder, "file" + ext);
        await DownloadUrlAsync(ctx, item.FileUrl, path).ConfigureAwait(false);
        ctx.Post(() => item.FilePath = path);
    }

    /// <summary>
    /// Downloads the playable media for an episode. Playlists are fetched exactly
    /// once: either straight through the HLS downloader (URL looks like a
    /// playlist) or by reading the body of the request that was sniffed.
    /// </summary>
    public static async Task<string> DownloadPlayableAsync(DownloadContext ctx, string url)
    {
        if (HlsSource(url))
        {
            return await DownloadHlsAsync(ctx, url, null).ConfigureAwait(false);
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await ctx.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.Ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var type = resp.Content.Headers.ContentType?.MediaType;
        if (HlsDownloader.LooksLikePlaylist(url, type))
        {
            var content = await resp.Content.ReadAsStringAsync(ctx.Ct).ConfigureAwait(false);
            return await DownloadHlsAsync(ctx, url, content).ConfigureAwait(false);
        }

        var ext = DownloadNames.GuessExt(url, ".mp4");
        var path = Path.Combine(ctx.Item.Folder, "video" + ext);
        await using var src = await resp.Content.ReadAsStreamAsync(ctx.Ct).ConfigureAwait(false);
        await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await CopyProgressAsync(ctx, src, dst, resp.Content.Headers.ContentLength).ConfigureAwait(false);
        return path;
    }

    private static async Task<string> DownloadHlsAsync(DownloadContext ctx, string url, string? content)
    {
        var dest = Path.Combine(ctx.Item.Folder, "hls");
        var progress = new Progress<HlsProgress>(p => ctx.Post(() =>
        {
            ctx.Item.PartsDone = p.PartsDone;
            ctx.Item.PartsTotal = p.PartsTotal;
            ctx.Item.BytesReceived = p.Bytes;
            ctx.Item.Progress = p.PartsTotal > 0 ? p.PartsDone / (double)p.PartsTotal : 0;
        }));
        return content is not null
            ? await ctx.Hls.DownloadFromContentAsync(url, content, dest, progress, ctx.Ct).ConfigureAwait(false)
            : await ctx.Hls.DownloadAsync(url, dest, progress, ctx.Ct).ConfigureAwait(false);
    }

    private static async Task DownloadUrlAsync(DownloadContext ctx, string url, string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await ctx.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.Ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var src = await resp.Content.ReadAsStreamAsync(ctx.Ct).ConfigureAwait(false);
        await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await CopyProgressAsync(ctx, src, dst, resp.Content.Headers.ContentLength).ConfigureAwait(false);
    }

    private static async Task CopyProgressAsync(
        DownloadContext ctx, Stream src, Stream dst, long? total)
    {
        var item = ctx.Item;
        var ct = ctx.Ct;
        var buffer = new byte[64 * 1024];
        long read = 0;
        if (total is > 0)
        {
            ctx.Post(() => item.BytesTotal = total);
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var n = await src.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            var snapshot = read;
            ctx.Post(() =>
            {
                item.BytesReceived = snapshot;
                if (total is > 0)
                {
                    item.Progress = snapshot / (double)total.Value;
                }
            });
        }
    }

    private static bool HlsSource(string url) =>
        HlsDownloader.LooksLikePlaylist(url)
        || url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Small path helpers shared by the download service and its processors.</summary>
internal static class DownloadNames
{
    public static string GuessExt(string url, string fallback)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (ext is { Length: > 1 } and { Length: < 8 }
                && ext.All(c => char.IsLetterOrDigit(c) || c == '.'))
            {
                return ext.ToLowerInvariant();
            }
        }
        catch (UriFormatException)
        {
        }
        return fallback;
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || c is ':' or '/' or '\\' ? '_' : c).ToArray();
        var s = new string(chars).Trim().Trim('.');
        return s.Length == 0 ? "item" : s.Length > 80 ? s[..80] : s;
    }
}
