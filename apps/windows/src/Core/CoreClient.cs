using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anibel.App.Services;

namespace Anibel.App.Core;

/// <summary>
/// Typed client over `anibel_core.dll`. Every call is a blocking FFI round-trip
/// executed on the thread pool: { id, op, args } -> { id, ok, value | error }.
/// Logs go to %TEMP%\anibel-debug.log with secrets redacted.
/// </summary>
public sealed class CoreClient : ICoreClient, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SettingsService _settings;
    private readonly ApiCache _cache;
    private readonly object _initLock = new();
    private long _handle = -1;
    private long _nextId;
    private bool _disposed;

    public CoreClient(SettingsService settings, ApiCache cache)
    {
        _settings = settings;
        _cache = cache;
    }

    public bool IsConnected => Interlocked.Read(ref _handle) >= 0;

    private long EnsureHandle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var handle = Interlocked.Read(ref _handle);
        if (handle >= 0)
        {
            return handle;
        }
        lock (_initLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            handle = Interlocked.Read(ref _handle);
            if (handle >= 0)
            {
                return handle;
            }
            var config = new
            {
                baseUrl = _settings.ApiBaseUrl,
                videoBaseUrl = _settings.VideoBaseUrl,
            };
            handle = AnibelCoreNative.anibel_core_init(JsonSerializer.Serialize(config, Json));
            if (handle < 0)
            {
                throw new InvalidOperationException("anibel_core_init failed — is anibel_core.dll next to the exe?");
            }
            Interlocked.Exchange(ref _handle, handle);
            return handle;
        }
    }

    // ------------------------------------------------------------------
    // generic call
    // ------------------------------------------------------------------

    public async Task<T> CallAsync<T>(string op, object? args = null, CancellationToken ct = default)
    {
        var raw = await CallRawAsync(op, args, ct).ConfigureAwait(false); // already unwrapped to `value`
        if (raw.ValueKind == JsonValueKind.Null)
        {
            return default!;
        }

        return raw.Deserialize<T>(Json) ?? default!;
    }

    public async Task<JsonElement> CallRawAsync(string op, object? args = null, CancellationToken ct = default)
    {
        var request = new
        {
            id = Interlocked.Increment(ref _nextId),
            op,
            args,
        };
        var requestJson = JsonSerializer.Serialize(request, Json);
        var argsJson = JsonSerializer.Serialize(args ?? new { }, Json);

        if (ApiCache.IsCacheable(op) && !ApiCache.IsBypassed
            && _cache.TryGet(op, argsJson, out var cached, out var stale))
        {
            if (stale)
            {
                _ = RefreshCacheAsync(op, requestJson, argsJson, CancellationToken.None);
            }
            using var cachedDoc = JsonDocument.Parse(cached);
            return cachedDoc.RootElement.Clone();
        }

        JsonElement? result = null;
        try
        {
            result = await CallRawCoreAsync(requestJson, ct).ConfigureAwait(false);
            var value = result.Value;
            if (ApiCache.IsCacheable(op))
            {
                _cache.Set(op, argsJson, value.GetRawText());
            }
            else
            {
                _cache.Invalidate(ApiCache.InvalidatedBy(op));
            }
            return value;
        }
        catch (Exception ex)
        {
            Log($"op={op} args={Shorten(Redact(requestJson))}\n  -> {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            if (result is null)
            {
                Log($"op={op} threw (see above)");
            }
        }
    }

    private async Task RefreshCacheAsync(string op, string requestJson, string argsJson, CancellationToken ct)
    {
        try
        {
            var value = await CallRawCoreAsync(requestJson, ct).ConfigureAwait(false);
            _cache.Set(op, argsJson, value.GetRawText());
        }
        catch (Exception ex)
        {
            Diag.Log($"cache revalidate {op}: {ex.Message}");
        }
    }

    private async Task<JsonElement> CallRawCoreAsync(string requestJson, CancellationToken ct)
    {
        var handle = EnsureHandle();

        var responseJson = await Task.Run(() =>
        {
            var ptr = AnibelCoreNative.anibel_core_call(handle, requestJson);
            try
            {
                return Marshal.PtrToStringUTF8(ptr) ?? "{}";
            }
            finally
            {
                AnibelCoreNative.anibel_core_free(ptr);
            }
        }, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
        {
            return root.GetProperty("value").Clone();
        }

        var error = root.TryGetProperty("error", out var e)
            ? new CoreErrorDto(
                e.TryGetProperty("code", out var c) ? c.GetString() ?? "unknown" : "unknown",
                e.TryGetProperty("message", out var m) ? m.GetString() ?? "core error" : "core error")
            : new CoreErrorDto("unknown", "empty error from core");

        throw new CoreException(error.Code, error.Message);
    }

    private static string Shorten(string s) => s.Length > 220 ? s[..220] + "…" : s;

    /// <summary>Masks secret values (passwords, tokens) before they hit the log.</summary>
    private static string Redact(string requestJson)
    {
        try
        {
            var node = JsonNode.Parse(requestJson);
            if (node?["args"] is JsonObject args)
            {
                foreach (var key in new[] { "password", "token", "passwordNew" })
                {
                    if (args.ContainsKey(key))
                    {
                        args[key] = "***";
                    }
                }
            }
            return node?.ToJsonString(Json) ?? requestJson;
        }
        catch (JsonException)
        {
            return requestJson;
        }
    }

    private static void Log(string line)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "anibel-debug.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch
        {
            // logging must never break the app
        }
    }

    /// <summary>Drain queued core events (call from a ~60ms DispatcherQueueTimer).</summary>
    public IReadOnlyList<JsonElement> DrainEvents()
    {
        var handle = Interlocked.Read(ref _handle);
        if (handle < 0)
        {
            return [];
        }
        var ptr = AnibelCoreNative.anibel_core_events(handle);
        try
        {
            var json = Marshal.PtrToStringUTF8(ptr) ?? "[]";
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
        }
        finally
        {
            AnibelCoreNative.anibel_core_free(ptr);
        }
    }

    // ------------------------------------------------------------------
    // typed ops (M0/M1 read surface)
    // ------------------------------------------------------------------

    public Task<CoreVersion> GetVersionAsync() => CallAsync<CoreVersion>("version");

    public Task<List<MediaCard>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
        => CallAsync<List<MediaCard>>("search", new { query, limit }, ct);

    public Task<List<MediaCard>> TrendsAsync(string type = "all", string date = "week", int limit = 12, CancellationToken ct = default)
        => CallAsync<List<MediaCard>>("trends", new { type, date, limit }, ct);

    public Task<PaginationDto<MediaCard>> MediaListAsync(string mediaType, int offset = 0, int limit = 20, object? filters = null, CancellationToken ct = default)
        => CallAsync<PaginationDto<MediaCard>>("mediaList", new { mediaType, offset, limit, filters }, ct);

    public Task<PaginationDto<EpisodeDto>> EpisodesAsync(string mediaId, string type = "sub", int resource = 1, int? limit = null, CancellationToken ct = default)
        => CallAsync<PaginationDto<EpisodeDto>>("episodes", new { mediaId, type, resource, limit }, ct);

    public Task<List<EpisodeDto>> EpisodesMatrixAsync(string mediaId, CancellationToken ct = default)
        => CallAsync<List<EpisodeDto>>("episodesMatrix", new { mediaId }, ct);

    public Task<PlaybackIntentDto> ResolveEpisodeAsync(string videoIdOrUrl, CancellationToken ct = default)
        => CallAsync<PlaybackIntentDto>("resolveEpisode", new { url = videoIdOrUrl }, ct);

    public Task<MediaDetailDto?> MediaAsync(string slug, string? mediaType = null, CancellationToken ct = default)
        => CallAsync<MediaDetailDto?>("media", new { slug, mediaType }, ct);

    public Task<AnibelFiltersDto> FiltersAsync(string mediaType, CancellationToken ct = default)
        => CallAsync<AnibelFiltersDto>("filters", new { mediaType }, ct);

    public Task<PaginationDto<CommentDto>> CommentsAsync(string mediaId, string mediaType, int offset = 0, int limit = 20, CancellationToken ct = default)
        => CallAsync<PaginationDto<CommentDto>>("comments", new { mediaId, mediaType, offset, limit }, ct);

    public Task<CommentDto> AddCommentAsync(string mediaId, string mediaType, string content, string? replyTo = null, CancellationToken ct = default)
        => CallAsync<CommentDto>("addComment", new { mediaId, mediaType, content, replyTo }, ct);

    public Task<PaginationDto<ChapterDto>> ChaptersAsync(string mediaId, int? limit = 50, CancellationToken ct = default)
        => CallAsync<PaginationDto<ChapterDto>>("chapters", new { mediaId, limit }, ct);

    public Task<ChapterDto?> ChapterAsync(string slug, double chapter, CancellationToken ct = default)
        => CallAsync<ChapterDto?>("chapter", new { slug, chapter }, ct);

    public Task<LoginUserDto> LoginAsync(string username, string password, CancellationToken ct = default)
        => CallAsync<LoginUserDto>("login", new { username, password }, ct);

    public Task LogoutAsync(CancellationToken ct = default)
        => CallAsync<JsonElement>("logout", null, ct);

    // ------------------------------------------------------------------
    // session restore + personal mutations (M2)
    // ------------------------------------------------------------------

    /// <summary>Injects the persisted token into the core session (no network).</summary>
    public Task SetTokenAsync(string token, CancellationToken ct = default)
        => CallAsync<JsonElement>("setToken", new { token }, ct);

    public Task MarkAsAsync(string mediaId, string mediaType, string status, CancellationToken ct = default)
        => CallAsync<JsonElement>("markAs", new { mediaId, mediaType, status }, ct);

    public Task RemoveMarkAsync(string mediaId, string mediaType, string status, CancellationToken ct = default)
        => CallAsync<JsonElement>("removeMark", new { mediaId, mediaType, status }, ct);

    public Task AddFavoriteAsync(string mediaId, string mediaType, CancellationToken ct = default)
        => CallAsync<JsonElement>("addFavorite", new { mediaId, mediaType }, ct);

    public Task RemoveFavoriteAsync(string mediaId, string mediaType, CancellationToken ct = default)
        => CallAsync<JsonElement>("removeFavorite", new { mediaId, mediaType }, ct);

    public Task AddHistoryRecordAsync(string entityId, string type = "episode", CancellationToken ct = default)
        => CallAsync<JsonElement>("addHistoryRecord", new { entityId, type }, ct);

    public Task RemoveHistoryRecordAsync(string entityId, string type = "episode", CancellationToken ct = default)
        => CallAsync<JsonElement>("removeHistoryRecord", new { entityId, type }, ct);

    public Task<SlideDto[]> SliderAsync(int limit = 6, CancellationToken ct = default)
        => CallAsync<SlideDto[]>("slider", new { limit }, ct);

    public Task<MediaCard[]> UpdatesAsync(string type = "ALL", int offset = 0, int limit = 12, CancellationToken ct = default)
        => CallAsync<MediaCard[]>("updates", new { type, offset, limit }, ct);

    // ------------------------------------------------------------------
    // profiles (public, no token needed)
    // ------------------------------------------------------------------

    public Task<ProfileDto?> UserAsync(string username, CancellationToken ct = default)
        => CallAsync<ProfileDto?>("user", new { username }, ct);

    public Task<PaginationDto<MediaCard>> FavoritesAsync(string username, string? mediaType = null, int offset = 0, int limit = 60, CancellationToken ct = default)
        => CallAsync<PaginationDto<MediaCard>>("favorites", new { username, mediaType, offset, limit }, ct);

    public Task<PaginationDto<MarkEntryDto>> MarksAsync(string username, string? mediaType = null, int offset = 0, int limit = 60, CancellationToken ct = default)
        => CallAsync<PaginationDto<MarkEntryDto>>("marks", new { username, mediaType, offset, limit }, ct);

    public Task<StatusCountersDto?> StatusAsync(string username, string mediaType, CancellationToken ct = default)
        => CallAsync<StatusCountersDto?>("status", new { username, mediaType }, ct);

    public void Dispose()
    {
        _disposed = true;
        var h = Interlocked.Exchange(ref _handle, -1);
        if (h >= 0)
        {
            AnibelCoreNative.anibel_core_shutdown(h);
        }
    }
}
