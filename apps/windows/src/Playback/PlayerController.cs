using System.Security.Cryptography;
using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App.Playback;

public sealed class PlayerSurfaces
{
    public required SwapChainPanel VideoPanel { get; init; }
    public required Panel EmbedHost { get; init; }
}

/// <summary>
/// Resolve → cache assets → pick engine → resume/history. Views stay dumb.
/// </summary>
public sealed class PlayerController : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly ICoreClient _core;
    private readonly SessionService _session;
    private readonly DownloadService? _downloads;
    private readonly IResumeStore _resumeStore;
    private CancellationTokenSource _cts = new();
    private IPlayerEngine? _engine;
    private bool _disposed;
    private bool _historySubmitted;
    private bool _resumeApplied;
    private double _lastSavedPos;
    private DateTime _lastSaveAt = DateTime.MinValue;
    private string? _episodeId;

    public PlayerController(
        ICoreClient core,
        SessionService session,
        DownloadService? downloads = null,
        IResumeStore? resumeStore = null)
    {
        _core = core;
        _session = session;
        _downloads = downloads;
        _resumeStore = resumeStore ?? ResumeStoreAdapter.Instance;
    }

    public event Action? Ready;
    public event Action? Ended;
    public event Action<string>? Error;
    public event Action<double, double>? PositionChanged;
    public event Action<bool>? PauseChanged;

    public IPlayerEngine? Engine => _engine;
    /// <summary>Transport controls; null for engines without native transport (embed).</summary>
    public IPlaybackControls? Controls => _engine as IPlaybackControls;
    public PlaybackIntentDto? Intent { get; private set; }
    public bool IsDisposed => _disposed;

    public async Task StartAsync(string episodeUrl, string? episodeId, PlayerSurfaces surfaces, bool preferDubAudio = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelInFlight();
        var ct = _cts.Token;
        _episodeId = episodeId;
        _historySubmitted = false;
        _resumeApplied = false;

        if (_downloads?.FindCompletedEpisode(episodeId) is { } local
            && local.HasVideo)
        {
            await StartLocalAsync(local.VideoPath!, local.SubtitlePaths, local.FontPaths, episodeId, surfaces, preferDubAudio)
                .ConfigureAwait(true);
            return;
        }

        var intent = await _core.ResolveEpisodeAsync(episodeUrl, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        Intent = intent;

        if (intent.Kind == "embed")
        {
            await AttachAsync(new WebView2Engine(surfaces.EmbedHost), intent, [], ct).ConfigureAwait(true);
            return;
        }
        if (intent.Kind != "native")
        {
            Error?.Invoke($"Невядомы тып: {intent.Kind}");
            return;
        }

        var subPaths = await PrepareAssetsAsync(intent, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();

        var mpv = new MpvEngine();
        mpv.PreferDubAudio(preferDubAudio);
        Wire(mpv);
        _engine = mpv;
        ct.ThrowIfCancellationRequested();
        mpv.Initialize(surfaces.VideoPanel, GetPlaybackRoot(), 1920, 1080);
        await mpv.LoadAsync(intent, subPaths, ct).ConfigureAwait(true);
    }

    public async Task StartLocalAsync(
        string videoPath,
        IReadOnlyList<string> subPaths,
        IReadOnlyList<string>? fontPaths,
        string? episodeId,
        PlayerSurfaces surfaces,
        bool preferDubAudio = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelInFlight();
        var ct = _cts.Token;
        _episodeId = episodeId;
        _historySubmitted = false;
        _resumeApplied = false;

        if (fontPaths is { Count: > 0 })
        {
            InstallFonts(fontPaths);
        }

        var intent = new PlaybackIntentDto(
            "native",
            null,
            null,
            videoPath,
            null,
            subPaths.FirstOrDefault(),
            [],
            [],
            null);
        Intent = intent;
        ct.ThrowIfCancellationRequested();
        var mpv = new MpvEngine();
        mpv.PreferDubAudio(preferDubAudio);
        Wire(mpv);
        _engine = mpv;
        mpv.Initialize(surfaces.VideoPanel, GetPlaybackRoot(), 1920, 1080);
        await mpv.LoadAsync(intent, subPaths, ct).ConfigureAwait(true);
    }

    private static void InstallFonts(IReadOnlyList<string> fontPaths)
    {
        var fontsDir = Path.Combine(GetPlaybackRoot(), "fonts");
        Directory.CreateDirectory(fontsDir);
        foreach (var src in fontPaths)
        {
            if (string.IsNullOrEmpty(src) || !File.Exists(src))
            {
                continue;
            }
            var dest = Path.Combine(fontsDir, Path.GetFileName(src));
            if (!File.Exists(dest))
            {
                try { File.Copy(src, dest); }
                catch (IOException) { }
            }
        }
    }

    private async Task AttachAsync(
        IPlayerEngine engine,
        PlaybackIntentDto intent,
        IReadOnlyList<string> subPaths,
        CancellationToken ct)
    {
        Wire(engine);
        _engine = engine;
        ct.ThrowIfCancellationRequested();
        await engine.LoadAsync(intent, subPaths, ct).ConfigureAwait(true);
    }

    private void Wire(IPlayerEngine engine)
    {
        engine.Ready += OnReady;
        engine.Ended += OnEnded;
        engine.Error += OnError;
        engine.PositionChanged += OnPositionChanged;
        engine.PauseChanged += OnPauseChanged;
    }

    private void Unwire(IPlayerEngine engine)
    {
        engine.Ready -= OnReady;
        engine.Ended -= OnEnded;
        engine.Error -= OnError;
        engine.PositionChanged -= OnPositionChanged;
        engine.PauseChanged -= OnPauseChanged;
    }

    private void OnError(string message) => Error?.Invoke(message);

    private void OnPauseChanged(bool paused) => PauseChanged?.Invoke(paused);

    private void OnReady()
    {
        if (_disposed)
        {
            return;
        }
        Ready?.Invoke();

        if (!_resumeApplied && _episodeId is { } id && _engine is not null)
        {
            _resumeApplied = true;
            var pos = _resumeStore.Get(id);
            if (pos > 30 && Controls is { } controls && pos < controls.Duration - 15)
            {
                controls.Seek(Math.Max(0, pos - 5));
            }
        }

        if (!_historySubmitted && _session.HasSession && _episodeId is { } epId)
        {
            _historySubmitted = true;
            _ = SubmitHistoryAsync(epId);
        }
    }

    private void OnEnded()
    {
        if (_disposed)
        {
            return;
        }
        if (_episodeId is { } id)
        {
            _resumeStore.Clear(id);
            if (_session.HasSession)
            {
                _ = SubmitHistoryAsync(id);
            }
        }
        Ended?.Invoke();
    }

    private void OnPositionChanged(double pos, double dur)
    {
        PositionChanged?.Invoke(pos, dur);
        SaveResume(force: false);
    }

    // Deliberately does not short-circuit on _disposed: TearDownEngine must be
    // able to force-save after Dispose() set the flag (teardown order: flag →
    // cancel → TearDownEngine). "No engine" is the only stop condition here.
    public void SaveResume(bool force = false)
    {
        if (_episodeId is not { } id || Controls is not { } controls || controls.Position < 5)
        {
            return;
        }
        if (!force && DateTime.UtcNow - _lastSaveAt < TimeSpan.FromSeconds(5))
        {
            return;
        }
        if (!force && Math.Abs(controls.Position - _lastSavedPos) < 1)
        {
            return;
        }
        _lastSaveAt = DateTime.UtcNow;
        _lastSavedPos = controls.Position;
        _resumeStore.Set(id, controls.Position);
    }

    private async Task SubmitHistoryAsync(string episodeId)
    {
        try
        {
            await _core.AddHistoryRecordAsync(episodeId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[history] {ex.Message}");
        }
    }

    public void TogglePause() => Controls?.TogglePause();
    public void Seek(double seconds) => Controls?.Seek(seconds);
    public void SetVolume(double volume) => Controls?.SetVolume(volume);
    public void SetMute(bool mute) => Controls?.SetMute(mute);
    public void SetSubTrack(long id) => Controls?.SetSubTrack(id);
    public void Resize(uint width, uint height) => Controls?.Resize(width, height);
    public void InvalidateSurface() => Controls?.InvalidateSurface();

    public static string GetPlaybackRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Anibel", "playback");

    private static async Task<List<string>> PrepareAssetsAsync(PlaybackIntentDto intent, CancellationToken ct)
    {
        var root = GetPlaybackRoot();
        var fontsDir = Path.Combine(root, "fonts");
        Directory.CreateDirectory(fontsDir);

        var subPaths = new List<string>();
        foreach (var sub in intent.Subtitles ?? [])
        {
            ct.ThrowIfCancellationRequested();
            var name = SubtitlePicker.SafeFileName(sub.Url, sub.Label, subPaths.Count);
            var path = await CachedDownloadAsync(Path.Combine(root, "subs"), sub.Url, name, ct);
            if (path is not null)
            {
                subPaths.Add(path);
            }
        }

        foreach (var font in intent.Fonts ?? [])
        {
            ct.ThrowIfCancellationRequested();
            await CachedDownloadAsync(fontsDir, font.Url, ".ttf", ct);
        }

        return subPaths;
    }

    private static async Task<string?> CachedDownloadAsync(string dir, string url, string extension, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
            var suffix = extension.StartsWith('.') ? hash + extension : hash + "_" + extension;
            var path = Path.Combine(dir, suffix);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                return path;
            }
            var data = await Http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(path, data, ct);
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Diag.Log($"asset download failed ({url}): {ex.Message}");
            return null;
        }
    }

    private void CancelInFlight()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        TearDownEngine();
    }

    private void TearDownEngine()
    {
        SaveResume(force: true);
        if (_engine is { } engine)
        {
            Unwire(engine);
            engine.Dispose();
            _engine = null;
        }
    }

    /// <summary>
    /// Test seam: installs a fake engine and wires events exactly like the
    /// production start paths, without surfaces/assets. PlaybackControllerTests
    /// uses this to drive the controller deterministically (no UI thread).
    /// </summary>
    internal void AttachForTesting(IPlayerEngine engine, string? episodeId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TearDownEngine();
        _episodeId = episodeId;
        _historySubmitted = false;
        _resumeApplied = false;
        Wire(engine);
        _engine = engine;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        TearDownEngine();
        _cts.Dispose();
    }
}
