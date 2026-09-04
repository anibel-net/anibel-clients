using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Anibel.App.Services;

public readonly record struct HlsProgress(int PartsDone, int PartsTotal, long Bytes);

public sealed class HlsMediaPlaylist
{
    public string? MapUri { get; init; }
    public string? KeyMethod { get; init; }
    public string? KeyUri { get; init; }
    public int TargetDuration { get; init; }
    public List<string> SegmentUris { get; init; } = [];
    public List<double> Durations { get; init; } = [];
}

/// <summary>
/// Fetch an HLS master/media playlist, download segments, rewrite a local
/// playlist, and concatenate TS/CMAF into a single playable file when possible.
/// </summary>
public sealed class HlsDownloader
{
    private static readonly Regex AttrUri = new(@"URI=(?:""([^""]+)""|([^,]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Bandwidth = new(@"BANDWIDTH=(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;

    public HlsDownloader(HttpClient http) => _http = http;

    public static bool LooksLikePlaylist(string url, string? contentType = null, string? sniff = null)
    {
        if (!string.IsNullOrEmpty(contentType)
            && contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (sniff is { Length: > 0 } && sniff.StartsWith("#EXTM3U", StringComparison.Ordinal))
        {
            return true;
        }
        var path = url;
        try { path = new Uri(url).AbsolutePath; }
        catch (UriFormatException) { }
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMaster(string content) =>
        content.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase);

    public static string? PickBestVariantUri(string content, string playlistUrl)
    {
        var lines = SplitLines(content);
        var bestBw = -1L;
        string? best = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var bw = 0L;
            var m = Bandwidth.Match(line);
            if (m.Success)
            {
                long.TryParse(m.Groups[1].Value, out bw);
            }
            var uri = NextUri(lines, i + 1);
            if (uri is null)
            {
                continue;
            }
            if (bw >= bestBw)
            {
                bestBw = bw;
                best = Resolve(playlistUrl, uri);
            }
        }
        return best;
    }

    public static HlsMediaPlaylist ParseMedia(string content, string playlistUrl)
    {
        var lines = SplitLines(content);
        string? map = null;
        string? keyMethod = null;
        string? keyUri = null;
        var target = 0;
        var uris = new List<string>();
        var durs = new List<double>();
        double pendingDur = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("#EXT-X-MAP", StringComparison.OrdinalIgnoreCase))
            {
                var uri = ReadAttrUri(line);
                if (uri is not null)
                {
                    map = Resolve(playlistUrl, uri);
                }
                continue;
            }
            if (line.StartsWith("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase))
            {
                keyMethod = ReadAttr(line, "METHOD");
                var uri = ReadAttrUri(line);
                if (uri is not null)
                {
                    keyUri = Resolve(playlistUrl, uri);
                }
                continue;
            }
            if (line.StartsWith("#EXT-X-TARGETDURATION", StringComparison.OrdinalIgnoreCase))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    int.TryParse(line[(colon + 1)..].Trim(), out target);
                }
                continue;
            }
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                var colon = line.IndexOf(':');
                var rest = colon > 0 ? line[(colon + 1)..] : "";
                var comma = rest.IndexOf(',');
                var num = comma >= 0 ? rest[..comma] : rest;
                double.TryParse(num.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out pendingDur);
                continue;
            }
            if (line.StartsWith('#') || line.Length == 0)
            {
                continue;
            }
            uris.Add(Resolve(playlistUrl, line));
            durs.Add(pendingDur);
            pendingDur = 0;
        }

        return new HlsMediaPlaylist
        {
            MapUri = map,
            KeyMethod = keyMethod,
            KeyUri = keyUri,
            TargetDuration = target,
            SegmentUris = uris,
            Durations = durs,
        };
    }

    public Task<string> DownloadAsync(
        string playlistUrl,
        string destDir,
        IProgress<HlsProgress>? progress,
        CancellationToken ct)
        => DownloadFromContentAsync(playlistUrl, null, destDir, progress, ct);

    /// <summary>
    /// Download an HLS stream when the playlist text is already in hand (no refetch).
    /// The caller may pass the master or media playlist body; variants are still
    /// fetched by URL when the content is a master playlist.
    /// </summary>
    public async Task<string> DownloadFromContentAsync(
        string playlistUrl,
        string? content,
        string destDir,
        IProgress<HlsProgress>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var text = content ?? await _http.GetStringAsync(playlistUrl, ct).ConfigureAwait(false);
        if (IsMaster(text))
        {
            var variant = PickBestVariantUri(text, playlistUrl)
                ?? throw new InvalidOperationException("HLS master playlist has no variants.");
            playlistUrl = variant;
            text = await _http.GetStringAsync(playlistUrl, ct).ConfigureAwait(false);
        }

        var media = ParseMedia(text, playlistUrl);
        if (!string.IsNullOrEmpty(media.KeyMethod)
            && !string.Equals(media.KeyMethod, "NONE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Шыфраванае відэа нельга спампаваць.");
        }
        if (media.SegmentUris.Count == 0)
        {
            throw new InvalidOperationException("У плэйлісце няма сегментаў.");
        }

        var segsDir = Path.Combine(destDir, "segs");
        Directory.CreateDirectory(segsDir);

        var parts = new List<(string Url, string Path, string Name)>();
        string? mapPath = null;
        string? mapName = null;
        if (media.MapUri is { Length: > 0 } mapUri)
        {
            mapName = "init" + GuessExt(mapUri, ".mp4");
            mapPath = Path.Combine(segsDir, mapName);
            parts.Add((mapUri, mapPath, mapName));
        }
        for (var i = 0; i < media.SegmentUris.Count; i++)
        {
            var url = media.SegmentUris[i];
            var name = $"seg{i:00000}{GuessExt(url, mapPath is null ? ".ts" : ".m4s")}";
            parts.Add((url, Path.Combine(segsDir, name), name));
        }

        long bytes = 0;
        var done = 0;
        var gate = new SemaphoreSlim(4);
        var errors = new List<Exception>();
        var tasks = parts.Select(async part =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                var n = await CopyAsync(part.Url, part.Path, ct).ConfigureAwait(false);
                var finished = Interlocked.Increment(ref done);
                var totalBytes = Interlocked.Add(ref bytes, n);
                progress?.Report(new HlsProgress(finished, parts.Count, totalBytes));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (errors) { errors.Add(ex); }
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Не ўдалося спампаваць сегменты ({errors.Count}): {errors[0].Message}");
        }

        var playlistPath = Path.Combine(destDir, "playlist.m3u8");
        await File.WriteAllTextAsync(playlistPath, BuildLocalPlaylist(media, mapName, parts, mapPath is not null), ct)
            .ConfigureAwait(false);

        var concat = TryConcat(destDir, mapPath, parts);
        return concat ?? playlistPath;
    }

    /// <summary>
    /// Concatenate TS segments into one playable file. fMP4 (CMAF) is NOT
    /// concatenated: byte-appending init + m4s chunks without rewriting tfdt/trun
    /// produces broken output, so null is returned and the caller falls back to
    /// the local playlist.m3u8 (segments are already downloaded and referenced).
    /// </summary>
    private static string? TryConcat(
        string destDir,
        string? mapPath,
        List<(string Url, string Path, string Name)> parts)
    {
        if (mapPath is not null)
        {
            return null;
        }
        try
        {
            var dest = Path.Combine(destDir, "video.ts");
            using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read);
            foreach (var part in parts)
            {
                using var input = File.OpenRead(part.Path);
                input.CopyTo(output);
            }
            return dest;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildLocalPlaylist(
        HlsMediaPlaylist media,
        string? mapName,
        List<(string Url, string Path, string Name)> parts,
        bool hasMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:7");
        if (media.TargetDuration > 0)
        {
            sb.AppendLine($"#EXT-X-TARGETDURATION:{media.TargetDuration}");
        }
        sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
        sb.AppendLine("#EXT-X-PLAYLIST-TYPE:VOD");
        var segStart = 0;
        if (hasMap && mapName is not null)
        {
            sb.AppendLine($"#EXT-X-MAP:URI=\"segs/{mapName}\"");
            segStart = 1;
        }
        for (var i = 0; i < media.SegmentUris.Count; i++)
        {
            var dur = i < media.Durations.Count ? media.Durations[i] : 0;
            sb.AppendLine($"#EXTINF:{dur.ToString("0.###", CultureInfo.InvariantCulture)},");
            var name = parts[segStart + i].Name;
            sb.AppendLine($"segs/{name}");
        }
        sb.AppendLine("#EXT-X-ENDLIST");
        return sb.ToString();
    }

    private async Task<long> CopyAsync(string url, string path, CancellationToken ct)
    {
        const int attempts = 3;
        Exception? last = null;
        for (var i = 0; i < attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
                return dst.Length;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                await Task.Delay(400 * (i + 1), ct).ConfigureAwait(false);
            }
        }
        throw last ?? new InvalidOperationException(url);
    }

    public static string Resolve(string baseUrl, string maybeRelative)
    {
        if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out var abs))
        {
            return abs.ToString();
        }
        return new Uri(new Uri(baseUrl), maybeRelative).ToString();
    }

    private static string GuessExt(string url, string fallback)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(url).AbsolutePath);
            if (ext is { Length: > 1 } and { Length: < 8 })
            {
                return ext;
            }
        }
        catch (UriFormatException)
        {
        }
        return fallback;
    }

    private static string[] SplitLines(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None)
            .Select(l => l.Trim())
            .ToArray();

    private static string? NextUri(string[] lines, int start)
    {
        for (var i = start; i < lines.Length; i++)
        {
            if (lines[i].Length == 0 || lines[i].StartsWith('#'))
            {
                continue;
            }
            return lines[i];
        }
        return null;
    }

    private static string? ReadAttrUri(string line)
    {
        var m = AttrUri.Match(line);
        if (!m.Success)
        {
            return null;
        }
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value.Trim();
    }

    private static string? ReadAttr(string line, string name)
    {
        var m = Regex.Match(line, name + @"=([^,]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim().Trim('"') : null;
    }
}

