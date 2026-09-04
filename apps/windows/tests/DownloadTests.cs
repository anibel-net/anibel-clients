using System.Text.Json;
using Anibel.App.Core;
using Anibel.App.Services;
using Xunit;

namespace Anibel.App.Tests;

public class HlsPlaylistTests
{
    [Fact]
    public void PickBestVariant_selects_highest_bandwidth()
    {
        const string master = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360
            low.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080
            hi.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720
            mid.m3u8
            """;
        var uri = HlsDownloader.PickBestVariantUri(master, "https://cdn.example/dash/master.m3u8");
        Assert.Equal("https://cdn.example/dash/hi.m3u8", uri);
    }

    [Fact]
    public void ParseMedia_reads_map_and_segments()
    {
        const string media = """
            #EXTM3U
            #EXT-X-VERSION:7
            #EXT-X-TARGETDURATION:6
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:6.0,
            seg0.m4s
            #EXTINF:5.5,
            seg1.m4s
            #EXT-X-ENDLIST
            """;
        var parsed = HlsDownloader.ParseMedia(media, "https://cdn.example/dash/hi.m3u8");
        Assert.Equal("https://cdn.example/dash/init.mp4", parsed.MapUri);
        Assert.Equal(6, parsed.TargetDuration);
        Assert.Equal(2, parsed.SegmentUris.Count);
        Assert.Equal("https://cdn.example/dash/seg0.m4s", parsed.SegmentUris[0]);
        Assert.Equal(5.5, parsed.Durations[1]);
    }

    [Fact]
    public void ParseMedia_rejects_relative_resolution_against_absolute_base()
    {
        var parsed = HlsDownloader.ParseMedia(
            "#EXTM3U\n#EXTINF:1,\n../a.ts\n",
            "https://n3.anibel.stream/dash/id/playlist.m3u8");
        Assert.Equal("https://n3.anibel.stream/dash/a.ts", parsed.SegmentUris[0]);
    }

    [Fact]
    public void LooksLikePlaylist_by_extension_and_sniff()
    {
        Assert.True(HlsDownloader.LooksLikePlaylist("https://n/x/manifest.m3u8"));
        Assert.True(HlsDownloader.LooksLikePlaylist("https://n/x", "application/vnd.apple.mpegurl"));
        Assert.True(HlsDownloader.LooksLikePlaylist("https://n/x", sniff: "#EXTM3U\n"));
        Assert.False(HlsDownloader.LooksLikePlaylist("https://n/x/video.mp4"));
    }
}

public class DownloadServiceTests
{
    [Fact]
    public void EnqueueEpisode_persists_and_reloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "anibel-dl-" + Guid.NewGuid().ToString("n"));
        try
        {
            var core = new FakeCoreClient();
            using (var svc = new DownloadService(core, dir, new HttpClient(new NullHandler())))
            {
                var item = svc.EnqueueEpisode(new EpisodeDownloadRequest
                {
                    EpisodeId = "e1",
                    EpisodeUrl = "https://video.anibel.net/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                    MediaId = "m1",
                    MediaType = "anime",
                    Slug = "slug",
                    Title = "Тэст",
                    EpisodeLabel = "1",
                    EpisodeType = "dub",
                });
                Assert.Equal("ep:e1", item.Id);
                Assert.Equal(DownloadKind.Video, item.Kind);
                Assert.Contains(item, svc.Items);
            }

            using var reloaded = new DownloadService(new FakeCoreClient(), dir, new HttpClient(new NullHandler()));
            Assert.Single(reloaded.Items);
            Assert.Equal("Тэст", reloaded.Items[0].Title);
            Assert.Equal("ep:e1", reloaded.Items[0].Id);
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void Enqueue_is_idempotent_per_episode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "anibel-dl-" + Guid.NewGuid().ToString("n"));
        try
        {
            using var svc = new DownloadService(new FakeCoreClient(), dir, new HttpClient(new NullHandler()));
            var a = svc.EnqueueEpisode(new EpisodeDownloadRequest
            {
                EpisodeId = "e1",
                EpisodeUrl = "https://video.anibel.net/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                MediaId = "m1",
                MediaType = "anime",
                Slug = "slug",
                Title = "A",
            });
            var b = svc.EnqueueEpisode(new EpisodeDownloadRequest
            {
                EpisodeId = "e1",
                EpisodeUrl = "https://video.anibel.net/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                MediaId = "m1",
                MediaType = "anime",
                Slug = "slug",
                Title = "A",
            });
            Assert.Same(a, b);
            Assert.Single(svc.Items);
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void Chapter_and_audio_keys_differ()
    {
        Assert.Equal("ch:slug:12.5", DownloadItem.ChapterKey("slug", 12.5));
        Assert.Equal("aud:e1", DownloadItem.EpisodeKey("e1", true));
        Assert.Equal("ep:e1", DownloadItem.EpisodeKey("e1", false));
    }

    [Fact]
    public async Task AudioOnly_hls_videosource_ends_failed_with_explanation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "anibel-dl-" + Guid.NewGuid().ToString("n"));
        try
        {
            using var svc = new DownloadService(new AudioNullCore(), dir, new HttpClient(new NullHandler()));
            var item = svc.EnqueueEpisode(new EpisodeDownloadRequest
            {
                EpisodeId = "a1",
                EpisodeUrl = "https://video.anibel.net/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                MediaId = "m1",
                MediaType = "anime",
                Slug = "slug",
                Title = "Тэст",
                AudioOnly = true,
            });
            Assert.Equal(DownloadKind.Audio, item.Kind);

            await WaitUntilAsync(() => item.Status == DownloadStatus.Failed, 10_000);
            Assert.Equal(DownloadStatus.Failed, item.Status);
            Assert.Equal(
                "Аўдыё-спампаванне недаступнае для гэтай крыніцы: відэа-паток HLS без асобнай аўдыё-дарожкі.",
                item.Error);
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task HlsDownloader_fetches_segments_and_concats()
    {
        var handler = new ScriptedHandler();
        handler.Map["https://cdn.example/master.m3u8"] = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=1000
            https://cdn.example/media.m3u8
            """;
        handler.Map["https://cdn.example/media.m3u8"] = """
            #EXTM3U
            #EXT-X-TARGETDURATION:1
            #EXTINF:1,
            https://cdn.example/a.ts
            #EXTINF:1,
            https://cdn.example/b.ts
            #EXT-X-ENDLIST
            """;
        handler.Bytes["https://cdn.example/a.ts"] = [1, 2, 3];
        handler.Bytes["https://cdn.example/b.ts"] = [4, 5];

        var dir = Path.Combine(Path.GetTempPath(), "anibel-hls-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var dl = new HlsDownloader(new HttpClient(handler));
            var path = await dl.DownloadAsync("https://cdn.example/master.m3u8", dir, null, CancellationToken.None);
            Assert.True(File.Exists(path));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(path));
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task HlsDownloader_fmp4_falls_back_to_local_playlist()
    {
        var handler = new ScriptedHandler();
        handler.Map["https://cdn.example/fmp4/media.m3u8"] = """
            #EXTM3U
            #EXT-X-VERSION:7
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:1,
            https://cdn.example/fmp4/seg0.m4s
            #EXTINF:1,
            https://cdn.example/fmp4/seg1.m4s
            #EXT-X-ENDLIST
            """;
        handler.Bytes["https://cdn.example/fmp4/init.mp4"] = [0, 0, 0, 24];
        handler.Bytes["https://cdn.example/fmp4/seg0.m4s"] = [1, 2, 3];
        handler.Bytes["https://cdn.example/fmp4/seg1.m4s"] = [4, 5];

        var dir = Path.Combine(Path.GetTempPath(), "anibel-hls-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var dl = new HlsDownloader(new HttpClient(handler));
            var path = await dl.DownloadAsync("https://cdn.example/fmp4/media.m3u8", dir, null, CancellationToken.None);
            Assert.Equal(Path.Combine(dir, "playlist.m3u8"), path);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(Path.Combine(dir, "video.mp4")));
            Assert.False(File.Exists(Path.Combine(dir, "video.ts")));
            var playlist = File.ReadAllText(path);
            Assert.Contains("#EXT-X-MAP:URI=\"segs/init.mp4\"", playlist);
            Assert.Contains("segs/seg00000.m4s", playlist);
            Assert.True(File.Exists(Path.Combine(dir, "segs", "init.mp4")));
            Assert.True(File.Exists(Path.Combine(dir, "segs", "seg00000.m4s")));
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void ApiCache_roundtrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "anibel-cache-" + Guid.NewGuid().ToString("n"));
        try
        {
            var cache = new ApiCache(dir);
            cache.Set("mediaList", "{\"t\":\"anime\"}", "{\"docs\":[1]}");
            Assert.True(cache.TryGet("mediaList", "{\"t\":\"anime\"}", out var json, out var stale));
            Assert.Contains("docs", json);
            Assert.False(stale);
            cache.Invalidate("mediaList");
            Assert.False(cache.TryGet("mediaList", "{\"t\":\"anime\"}", out _, out _));
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task DownloadStore_roundtrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "anibel-store-" + Guid.NewGuid().ToString("n"));
        try
        {
            Directory.CreateDirectory(dir);
            var video = Path.Combine(dir, "video.mp4");
            await File.WriteAllBytesAsync(video, [1, 2, 3]);

            var store = new DownloadStore(Path.Combine(dir, "library.json"));
            store.Save([
                new DownloadItem
                {
                    Id = "ep:e1",
                    Kind = DownloadKind.Video,
                    Status = DownloadStatus.Completed,
                    MediaId = "m1",
                    MediaType = "anime",
                    Slug = "slug",
                    Title = "Тэст",
                    Subtitle = "эп. 1",
                    Folder = Path.Combine(dir, "items"),
                    VideoPath = video,
                    Progress = 1,
                    CreatedAt = DateTimeOffset.Now,
                },
                new DownloadItem
                {
                    Id = "ep:e2",
                    Kind = DownloadKind.Video,
                    Status = DownloadStatus.Completed,
                    Title = "Знік з дыска",
                    Folder = dir,
                    VideoPath = Path.Combine(dir, "missing.mp4"),
                },
                new DownloadItem
                {
                    Id = "ch:slug:1",
                    Kind = DownloadKind.Manga,
                    Status = DownloadStatus.Queued,
                    Slug = "slug",
                    Title = "Глава",
                    Chapter = 1,
                    Folder = Path.Combine(dir, "pages"),
                    Progress = 0,
                },
            ]);

            var loaded = store.Load();
            Assert.Equal(3, loaded.Count);

            var ep = loaded[0];
            Assert.Equal("ep:e1", ep.Id);
            Assert.Equal(DownloadKind.Video, ep.Kind);
            Assert.Equal(DownloadStatus.Completed, ep.Status);
            Assert.Equal("Тэст", ep.Title);
            Assert.Equal(video, ep.VideoPath);
            Assert.Equal(1, ep.Progress);

            Assert.Equal(DownloadStatus.Failed, loaded[1].Status);
            Assert.Equal("Файлы зніклі з дыска.", loaded[1].Error);

            Assert.Equal(DownloadStatus.Queued, loaded[2].Status);
            Assert.Equal(1, loaded[2].Chapter);
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task UiDispatcher_without_ui_thread_runs_inline()
    {
        var ui = new UiDispatcher();

        var posted = false;
        ui.Post(() => posted = true);
        Assert.True(posted);

        var value = 0;
        await ui.PostAsync(() => value = 42);
        Assert.Equal(42, value);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ui.PostAsync(() => throw new InvalidOperationException("boom")));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("Timed out waiting for condition.");
            }
            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Resolves episodes to a native intent with AudioSrc=null and an HLS VideoSrc.
    /// FakeCoreClient.ResolveEpisodeAsync is not virtual, so the interface is
    /// re-implemented here with delegation.
    /// </summary>
    private sealed class AudioNullCore : ICoreClient
    {
        private readonly FakeCoreClient _inner = new();

        public bool IsConnected => _inner.IsConnected;
        public Task<CoreVersion> GetVersionAsync() => _inner.GetVersionAsync();
        public Task<List<MediaCard>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
            => _inner.SearchAsync(query, limit, ct);
        public Task<List<MediaCard>> TrendsAsync(string type = "all", string date = "week", int limit = 12, CancellationToken ct = default)
            => _inner.TrendsAsync(type, date, limit, ct);
        public Task<PaginationDto<MediaCard>> MediaListAsync(string mediaType, int offset = 0, int limit = 20, object? filters = null, CancellationToken ct = default)
            => _inner.MediaListAsync(mediaType, offset, limit, filters, ct);
        public Task<PaginationDto<EpisodeDto>> EpisodesAsync(string mediaId, string type = "sub", int resource = 1, int? limit = null, CancellationToken ct = default)
            => _inner.EpisodesAsync(mediaId, type, resource, limit, ct);
        public Task<List<EpisodeDto>> EpisodesMatrixAsync(string mediaId, CancellationToken ct = default)
            => _inner.EpisodesMatrixAsync(mediaId, ct);
        public Task<PlaybackIntentDto> ResolveEpisodeAsync(string videoIdOrUrl, CancellationToken ct = default)
            => Task.FromResult(new PlaybackIntentDto(
                "native", null, null, "https://cdn.example/media.m3u8", null, null, [], [], 1));
        public Task<MediaDetailDto?> MediaAsync(string slug, string? mediaType = null, CancellationToken ct = default)
            => _inner.MediaAsync(slug, mediaType, ct);
        public Task<AnibelFiltersDto> FiltersAsync(string mediaType, CancellationToken ct = default)
            => _inner.FiltersAsync(mediaType, ct);
        public Task<PaginationDto<CommentDto>> CommentsAsync(string mediaId, string mediaType, int offset = 0, int limit = 20, CancellationToken ct = default)
            => _inner.CommentsAsync(mediaId, mediaType, offset, limit, ct);
        public Task<CommentDto> AddCommentAsync(string mediaId, string mediaType, string content, string? replyTo = null, CancellationToken ct = default)
            => _inner.AddCommentAsync(mediaId, mediaType, content, replyTo, ct);
        public Task<PaginationDto<ChapterDto>> ChaptersAsync(string mediaId, int? limit = 50, CancellationToken ct = default)
            => _inner.ChaptersAsync(mediaId, limit, ct);
        public Task<ChapterDto?> ChapterAsync(string slug, double chapter, CancellationToken ct = default)
            => _inner.ChapterAsync(slug, chapter, ct);
        public Task<LoginUserDto> LoginAsync(string username, string password, CancellationToken ct = default)
            => _inner.LoginAsync(username, password, ct);
        public Task LogoutAsync(CancellationToken ct = default) => _inner.LogoutAsync(ct);
        public Task SetTokenAsync(string token, CancellationToken ct = default) => _inner.SetTokenAsync(token, ct);
        public Task MarkAsAsync(string mediaId, string mediaType, string status, CancellationToken ct = default)
            => _inner.MarkAsAsync(mediaId, mediaType, status, ct);
        public Task RemoveMarkAsync(string mediaId, string mediaType, string status, CancellationToken ct = default)
            => _inner.RemoveMarkAsync(mediaId, mediaType, status, ct);
        public Task AddFavoriteAsync(string mediaId, string mediaType, CancellationToken ct = default)
            => _inner.AddFavoriteAsync(mediaId, mediaType, ct);
        public Task RemoveFavoriteAsync(string mediaId, string mediaType, CancellationToken ct = default)
            => _inner.RemoveFavoriteAsync(mediaId, mediaType, ct);
        public Task AddHistoryRecordAsync(string entityId, string type = "episode", CancellationToken ct = default)
            => _inner.AddHistoryRecordAsync(entityId, type, ct);
        public Task RemoveHistoryRecordAsync(string entityId, string type = "episode", CancellationToken ct = default)
            => _inner.RemoveHistoryRecordAsync(entityId, type, ct);
        public Task<SlideDto[]> SliderAsync(int limit = 6, CancellationToken ct = default)
            => _inner.SliderAsync(limit, ct);
        public Task<MediaCard[]> UpdatesAsync(string type = "ALL", int offset = 0, int limit = 12, CancellationToken ct = default)
            => _inner.UpdatesAsync(type, offset, limit, ct);
        public Task<ProfileDto?> UserAsync(string username, CancellationToken ct = default)
            => _inner.UserAsync(username, ct);
        public Task<PaginationDto<MediaCard>> FavoritesAsync(string username, string? mediaType = null, int offset = 0, int limit = 60, CancellationToken ct = default)
            => _inner.FavoritesAsync(username, mediaType, offset, limit, ct);
        public Task<PaginationDto<MarkEntryDto>> MarksAsync(string username, string? mediaType = null, int offset = 0, int limit = 60, CancellationToken ct = default)
            => _inner.MarksAsync(username, mediaType, offset, limit, ct);
        public Task<StatusCountersDto?> StatusAsync(string username, string mediaType, CancellationToken ct = default)
            => _inner.StatusAsync(username, mediaType, ct);
        public IReadOnlyList<JsonElement> DrainEvents() => _inner.DrainEvents();
    }

    private sealed class NullHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Map { get; } = new();
        public Dictionary<string, byte[]> Bytes { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (Map.TryGetValue(url, out var text))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(text),
                });
            }
            if (Bytes.TryGetValue(url, out var data))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(data),
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
