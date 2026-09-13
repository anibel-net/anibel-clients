using Anibel.App.Core;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App.Playback;

public sealed class PlayerSurfaces
{
    public required SwapChainPanel VideoPanel
    {
        get; init;
    }
    public required Panel EmbedHost
    {
        get; init;
    }
}
public sealed record OpenPlaybackDto(ulong SessionId, PlaybackIntentDto Intent, string[] SubtitlePaths, string ConfigDirectory, bool PreferDub);
public sealed record PlaybackActionDto(double? SeekTo);

/// <summary>Native engine adapter. Rust owns sources, assets, resume and history.</summary>
public sealed class PlayerController(ICoreClient core) : IDisposable
{
    private CancellationTokenSource _cts = new();
    private IPlayerEngine? _engine;
    private ulong _sessionId;
    private ulong _sequence;
    private bool _disposed;
    private Task _reports = Task.CompletedTask;
    private readonly HashSet<Task> _starts = [];

    public event Action? Ready;
    public event Action? Ended;
    public event Action<string>? Error;
    public event Action<double, double>? PositionChanged;
    public event Action<bool>? PauseChanged;
    public IPlayerEngine? Engine => _engine;
    public IPlaybackControls? Controls => _engine as IPlaybackControls;
    public PlaybackIntentDto? Intent
    {
        get; private set;
    }
    public bool IsDisposed => _disposed;

    public Task StartAsync(string url, string? episodeId, PlayerSurfaces surfaces, string? episodeType = null, string? downloadId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var start = StartCoreAsync(url, episodeId, surfaces, episodeType, downloadId);
        _starts.Add(start);
        _ = RemoveStartAsync(start);
        return start;
    }

    private async Task RemoveStartAsync(Task start)
    {
        try { await start; }
        catch { /* The caller displays the load error. */ }
        finally { _starts.Remove(start); }
    }

    private async Task StartCoreAsync(string url, string? episodeId, PlayerSurfaces surfaces, string? episodeType, string? downloadId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopEngine();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var opened = await core.CallAsync<OpenPlaybackDto>("playbackOpen", new
        {
            url,
            episodeId,
            downloadId,
            episodeType
        }, ct);
        if (ct.IsCancellationRequested || _disposed)
        {
            await core.CallAsync<PlaybackActionDto>("playbackReport", new
            {
                sessionId = opened.SessionId,
                sequence = 1,
                @event = "close"
            });
            ct.ThrowIfCancellationRequested();
            return;
        }
        _sessionId = opened.SessionId;
        _sequence = 0;
        Intent = opened.Intent;
        IPlayerEngine engine;
        if (Intent.Kind == PlaybackKind.Embed)
            engine = new WebView2Engine(surfaces.EmbedHost);
        else
        {
            var mpv = new MpvEngine(core);
            mpv.PreferDubAudio(opened.PreferDub);
            _engine = mpv;
            mpv.Initialize(surfaces.VideoPanel, opened.ConfigDirectory, 1920, 1080);
            engine = mpv;
        }
        _engine = engine;
        Wire(engine);
        await engine.LoadAsync(Intent, opened.SubtitlePaths, ct);
    }
    private void Wire(IPlayerEngine engine)
    {
        engine.Ready += OnReady;
        engine.Ended += OnEnded;
        engine.Error += OnError;
        engine.PositionChanged += OnPosition;
        engine.PauseChanged += OnPause;
    }
    private void Unwire(IPlayerEngine engine)
    {
        engine.Ready -= OnReady;
        engine.Ended -= OnEnded;
        engine.Error -= OnError;
        engine.PositionChanged -= OnPosition;
        engine.PauseChanged -= OnPause;
    }
    private void OnReady()
    {
        QueueReport("ready");
        Ready?.Invoke();
    }
    private void OnEnded()
    {
        QueueReport("ended");
        Ended?.Invoke();
    }
    private void OnPosition(double position, double duration)
    {
        PositionChanged?.Invoke(position, duration);
        QueueReport("position", position, duration);
    }
    private void OnError(string error) => Error?.Invoke(error);
    private void OnPause(bool paused) => PauseChanged?.Invoke(paused);
    private void QueueReport(string kind, double? position = null, double? duration = null)
    {
        if (_sessionId == 0)
            return;
        var id = _sessionId;
        var request = new
        {
            sessionId = id,
            sequence = checked(++_sequence),
            @event = kind,
            position = Math.Max(0, position ?? Controls?.Position ?? 0),
            duration = Math.Max(0, duration ?? Controls?.Duration ?? 0)
        };
        _reports = SendAfterAsync(_reports, id, request);
    }
    private async Task SendAfterAsync(Task previous, ulong id, object request)
    {
        try
        {
            await previous;
            var action = await core.CallAsync<PlaybackActionDto>("playbackReport", request);
            if (!_disposed && id == _sessionId && action.SeekTo is { } seek)
                Controls?.Seek(seek);
        }
        catch (Exception ex) { Anibel.App.Services.Diag.Log($"playback report: {ex.Message}"); }
    }
    public void TogglePause() => Controls?.TogglePause();
    public void Seek(double seconds) => Controls?.Seek(seconds);
    public void SetVolume(double volume) => Controls?.SetVolume(volume);
    public void SetMute(bool mute) => Controls?.SetMute(mute);
    public void SetSubTrack(long id) => Controls?.SetSubTrack(id);
    public void Resize(uint width, uint height) => Controls?.Resize(width, height);
    public void InvalidateSurface() => Controls?.InvalidateSurface();
    private void StopEngine()
    {
        _cts.Cancel();
        QueueReport("close");
        _sessionId = 0;
        if (_engine is { } engine)
        {
            Unwire(engine);
            engine.Dispose();
            _engine = null;
        }
    }
    internal void AttachForTesting(IPlayerEngine engine, ulong sessionId)
    {
        StopEngine();
        _engine = engine;
        _sessionId = sessionId;
        _sequence = 0;
        Wire(engine);
    }
    internal Task ReportsCompleted => DrainAsync();

    private async Task DrainAsync()
    {
        // A cancelled open can still return a core session which must be closed.
        try { await Task.WhenAll(_starts.ToArray()); }
        catch { /* Start errors have already been reported to the view. */ }
        await _reports;
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopEngine();
        _cts.Dispose();
    }
}
