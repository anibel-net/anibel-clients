using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Anibel.App.Core;

namespace Anibel.App.Services;

/// <summary>
/// Offline library: queue + persist downloads, play/read without the network.
/// Files live in %LocalAppData%\Anibel\downloads. The queue/state/UI owner —
/// network work lives in <see cref="DownloadProcessors"/>, persistence in
/// <see cref="DownloadStore"/>, UI marshaling in <see cref="UiDispatcher"/>.
/// </summary>
public sealed class DownloadService : IDisposable
{
    private readonly ICoreClient _core;
    private readonly HttpClient _http;
    private readonly string _root;
    private readonly string _postersDir;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly Dictionary<string, CancellationTokenSource> _cts = new();
    private readonly UiDispatcher _ui;
    private readonly DownloadStore _store;
    private readonly HlsDownloader _hls;
    private bool _ownsHttp;

    public ObservableCollection<DownloadItem> Items { get; } = new();

    public string Root => _root;

    public DownloadService(ICoreClient core)
        : this(core, null, null)
    {
    }

    public DownloadService(ICoreClient core, string? rootDirectory, HttpClient? http)
        : this(core, rootDirectory, http, null)
    {
    }

    public DownloadService(ICoreClient core, string? rootDirectory, HttpClient? http, UiDispatcher? ui)
    {
        _core = core;
        _root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Anibel", "downloads");
        Directory.CreateDirectory(_root);
        _postersDir = Path.Combine(_root, "posters");
        Directory.CreateDirectory(_postersDir);
        _store = new DownloadStore(Path.Combine(_root, "library.json"));
        _ui = ui ?? UiDispatcher.ForCurrentThread();
        if (http is null)
        {
            _http = CreateHttp();
            _ownsHttp = true;
        }
        else
        {
            _http = http;
        }
        _hls = new HlsDownloader(_http);
        Load();
        Kick();
    }

    public static HttpClient CreateHttp()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromHours(6),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Anibel.Net");
        http.DefaultRequestHeaders.Referrer = new Uri("https://video.anibel.net/");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return http;
    }

    public DownloadItem EnqueueEpisode(EpisodeDownloadRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EpisodeId);
        var id = DownloadItem.EpisodeKey(request.EpisodeId, request.AudioOnly);
        lock (_sync)
        {
            if (FindById(id) is { } existing)
            {
                if (existing.Status is DownloadStatus.Failed or DownloadStatus.Cancelled)
                {
                    existing.Error = null;
                    existing.Progress = 0;
                    existing.BytesReceived = 0;
                    SetStatus(existing, DownloadStatus.Queued);
                    Persist();
                    Kick();
                }
                return existing;
            }
        }

        var typeLabel = string.IsNullOrWhiteSpace(request.EpisodeType)
            ? ""
            : Ui.Language(request.EpisodeType);
        var subtitle = string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(request.EpisodeLabel) ? null : $"эп. {request.EpisodeLabel}",
            string.IsNullOrWhiteSpace(typeLabel) ? null : typeLabel,
            request.AudioOnly ? "аўдыё" : null,
        }.Where(x => x is not null));

        var item = new DownloadItem
        {
            Id = id,
            Kind = request.AudioOnly ? DownloadKind.Audio : DownloadKind.Video,
            MediaId = request.MediaId,
            MediaType = request.MediaType,
            Slug = request.Slug,
            Title = request.Title,
            Subtitle = subtitle,
            PosterUrl = request.PosterUrl,
            EpisodeUrl = request.EpisodeUrl,
            EpisodeId = request.EpisodeId,
            EpisodeType = request.EpisodeType,
            Folder = Path.Combine(_root, "items", DownloadNames.Sanitize(id)),
            CreatedAt = DateTimeOffset.Now,
        };
        AddItem(item);
        Persist();
        _ = CachePosterAsync(item);
        Kick();
        return item;
    }

    public DownloadItem EnqueueChapter(ChapterDownloadRequest request)
    {
        var id = DownloadItem.ChapterKey(request.Slug, request.Chapter);
        lock (_sync)
        {
            if (FindById(id) is { } existing)
            {
                if (existing.Status is DownloadStatus.Failed or DownloadStatus.Cancelled)
                {
                    existing.Error = null;
                    existing.Progress = 0;
                    SetStatus(existing, DownloadStatus.Queued);
                    Persist();
                    Kick();
                }
                return existing;
            }
        }

        var item = new DownloadItem
        {
            Id = id,
            Kind = DownloadKind.Manga,
            MediaId = request.MediaId,
            MediaType = request.MediaType,
            Slug = request.Slug,
            Title = request.Title,
            Subtitle = string.IsNullOrWhiteSpace(request.ChapterTitle)
                ? $"Глава {request.Chapter:0.##}"
                : request.ChapterTitle,
            PosterUrl = request.PosterUrl,
            Chapter = request.Chapter,
            ChapterId = request.ChapterId,
            ChapterList = request.ChapterList,
            Folder = Path.Combine(_root, "items", DownloadNames.Sanitize(id)),
            CreatedAt = DateTimeOffset.Now,
        };
        AddItem(item);
        Persist();
        _ = CachePosterAsync(item);
        Kick();
        return item;
    }

    public DownloadItem EnqueueFile(FileDownloadRequest request)
    {
        var id = DownloadItem.FileKey(request.MediaId);
        lock (_sync)
        {
            if (FindById(id) is { } existing)
            {
                if (existing.Status is DownloadStatus.Failed or DownloadStatus.Cancelled)
                {
                    existing.Error = null;
                    existing.Progress = 0;
                    SetStatus(existing, DownloadStatus.Queued);
                    Persist();
                    Kick();
                }
                return existing;
            }
        }

        var item = new DownloadItem
        {
            Id = id,
            Kind = DownloadKind.File,
            MediaId = request.MediaId,
            MediaType = request.MediaType,
            Slug = request.Slug,
            Title = request.Title,
            Subtitle = request.Subtitle ?? "Файл",
            PosterUrl = request.PosterUrl,
            FileUrl = request.FileUrl,
            Folder = Path.Combine(_root, "items", DownloadNames.Sanitize(id)),
            CreatedAt = DateTimeOffset.Now,
        };
        AddItem(item);
        Persist();
        _ = CachePosterAsync(item);
        Kick();
        return item;
    }

    public DownloadItem? FindById(string id)
    {
        lock (_sync)
        {
            return Items.FirstOrDefault(x => x.Id == id);
        }
    }

    public DownloadItem? FindCompletedEpisode(string? episodeId, bool audioOnly = false)
    {
        if (string.IsNullOrEmpty(episodeId))
        {
            return null;
        }
        var item = FindById(DownloadItem.EpisodeKey(episodeId, audioOnly));
        return item is { Status: DownloadStatus.Completed, CanPlay: true } ? item : null;
    }

    public DownloadItem? FindCompletedChapter(string slug, double chapter)
    {
        var item = FindById(DownloadItem.ChapterKey(slug, chapter));
        return item is { Status: DownloadStatus.Completed, CanPlay: true } ? item : null;
    }

    public void Cancel(DownloadItem item)
    {
        lock (_sync)
        {
            if (_cts.TryGetValue(item.Id, out var cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
        if (item.IsActive)
        {
            SetStatus(item, DownloadStatus.Cancelled);
            Persist();
        }
    }

    public void Retry(DownloadItem item)
    {
        if (item.Status is not (DownloadStatus.Failed or DownloadStatus.Cancelled))
        {
            return;
        }
        item.Error = null;
        item.Progress = 0;
        item.BytesReceived = 0;
        item.PartsDone = 0;
        SetStatus(item, DownloadStatus.Queued);
        Persist();
        Kick();
    }

    public void Delete(DownloadItem item)
    {
        Cancel(item);
        try
        {
            if (Directory.Exists(item.Folder))
            {
                Directory.Delete(item.Folder, true);
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"download delete folder: {ex.Message}");
        }
        _ui.Post(() =>
        {
            lock (_sync)
            {
                Items.Remove(item);
            }
            Persist();
        });
    }

    public long TotalBytes()
    {
        lock (_sync)
        {
            return Items.Sum(i => i.OnDiskBytes());
        }
    }

    public string TotalBytesLabel() => DownloadItem.FormatBytes(TotalBytes());

    public int ActiveCount()
    {
        lock (_sync)
        {
            return Items.Count(i => i.IsActive);
        }
    }

    public async Task CopyToFolderAsync(DownloadItem item, string destFolder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destFolder);
        var name = DownloadNames.Sanitize($"{item.Title} - {item.Subtitle}".Trim(' ', '-'));
        if (item.Kind == DownloadKind.Manga)
        {
            var dir = Path.Combine(destFolder, name);
            Directory.CreateDirectory(dir);
            var n = 1;
            foreach (var src in item.ImagePaths.Where(File.Exists))
            {
                ct.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(src);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                File.Copy(src, Path.Combine(dir, $"{n:000}{ext}"), overwrite: true);
                n++;
            }
            return;
        }

        var file = item.Kind == DownloadKind.Audio
            ? item.AudioPath ?? item.VideoPath ?? item.FilePath
            : item.VideoPath ?? item.AudioPath ?? item.FilePath;
        if (file is null || !File.Exists(file))
        {
            throw new InvalidOperationException("Няма файла для захавання.");
        }
        var dest = Path.Combine(destFolder, name + Path.GetExtension(file));
        await Task.Run(() => File.Copy(file, dest, overwrite: true), ct).ConfigureAwait(false);
        foreach (var sub in item.SubtitlePaths.Where(File.Exists))
        {
            File.Copy(sub, Path.Combine(destFolder, name + Path.GetExtension(sub)), overwrite: true);
        }
    }

    private void Kick() => _ = RunQueueAsync();

    private async Task RunQueueAsync()
    {
        if (!await _worker.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            while (true)
            {
                DownloadItem? next;
                lock (_sync)
                {
                    next = Items.FirstOrDefault(i => i.Status == DownloadStatus.Queued);
                }
                if (next is null)
                {
                    return;
                }
                await ProcessAsync(next).ConfigureAwait(false);
            }
        }
        finally
        {
            _worker.Release();
        }
    }

    private async Task ProcessAsync(DownloadItem item)
    {
        var cts = new CancellationTokenSource();
        lock (_sync)
        {
            _cts[item.Id] = cts;
        }
        var ct = cts.Token;
        await _ui.PostAsync(() =>
        {
            item.Status = DownloadStatus.Downloading;
            item.Error = null;
            item.Progress = 0;
        }).ConfigureAwait(false);
        try
        {
            var ctx = new DownloadContext(_core, _http, _hls, item, ct, _ui.Post);
            await DownloadProcessors.ProcessAsync(ctx).ConfigureAwait(false);
            await _ui.PostAsync(() =>
            {
                item.Progress = 1;
                item.CompletedAt = DateTimeOffset.Now;
                item.Status = DownloadStatus.Completed;
            }).ConfigureAwait(false);
            Persist();
        }
        catch (OperationCanceledException)
        {
            await _ui.PostAsync(() =>
            {
                if (item.Status == DownloadStatus.Downloading)
                {
                    item.Status = DownloadStatus.Cancelled;
                }
            }).ConfigureAwait(false);
            Persist();
        }
        catch (Exception ex)
        {
            Diag.Log($"download {item.Id}: {ex.Message}");
            await _ui.PostAsync(() =>
            {
                item.Error = Friendly(ex);
                item.Status = DownloadStatus.Failed;
            }).ConfigureAwait(false);
            Persist();
        }
        finally
        {
            lock (_sync)
            {
                _cts.Remove(item.Id);
            }
            cts.Dispose();
        }
    }

    private async Task CachePosterAsync(DownloadItem item)
    {
        var url = item.PosterUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        try
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
            var ext = DownloadNames.GuessExt(url, ".jpg");
            var path = Path.Combine(_postersDir, hash + ext);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                var data = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);
            }
            _ui.Post(() => item.PosterPath = path);
            Persist();
        }
        catch (Exception ex)
        {
            Diag.Log($"poster cache: {ex.Message}");
        }
    }

    private static string Friendly(Exception ex) =>
        ex is CoreException core ? core.UserMessage : ex.Message;

    private void AddItem(DownloadItem item) => _ui.Post(() =>
    {
        lock (_sync)
        {
            Items.Insert(0, item);
        }
    });

    private void SetStatus(DownloadItem item, DownloadStatus status) => _ui.Post(() => item.Status = status);

    private void Load()
    {
        foreach (var item in _store.Load())
        {
            Items.Add(item);
        }
    }

    private void Persist()
    {
        List<DownloadItem> items;
        lock (_sync)
        {
            items = [.. Items];
        }
        _store.Save(items);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var cts in _cts.Values)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
                cts.Dispose();
            }
            _cts.Clear();
        }
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
