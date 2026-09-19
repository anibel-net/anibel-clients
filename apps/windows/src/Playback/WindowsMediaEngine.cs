using Anibel.App.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage;

namespace Anibel.App.Playback;

/// <summary>Windows codecs and adaptive streaming, with a libass subtitle overlay.</summary>
public sealed class WindowsMediaEngine : IPlayerEngine, IPlaybackControls
{
    private static readonly System.Net.Http.HttpClient ManifestClient = new()
    { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    private readonly ICoreClient _core;
    private readonly MediaPlayerElement _surface;
    private readonly Image _overlay;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly MediaPlayer _player = new();
    private readonly MediaTimelineController _timeline = new();
    private readonly NativeSubtitleAssets _assets = new();
    private readonly string _configDirectory;
    private readonly bool _preferDub;
    private readonly List<MediaSource> _sources = [];
    private MediaPlayer? _audio;
    private MediaPlaybackItem? _item, _audioItem;
    private AdaptiveMediaSource? _adaptive;
    private (byte[] Data, Uri Uri, string ContentType)? _manifest;
    private readonly SemaphoreSlim _qualitySwitch = new(1, 1);
    private Dictionary<uint, (uint Width, uint Height)> _videoSizes = [];
    private AssRenderer? _subtitles;
    private bool _disposed;
    // Timeline.State changes asynchronously. Toggle the user's intent, not its delayed acknowledgement.
    private bool _paused = true;
    private double? _seekTarget;
    private double? _activeSeek;
    private long _subtitle;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _loadTask = Task.CompletedTask;

    public WindowsMediaEngine(ICoreClient core, MediaPlayerElement surface, Image overlay, string configDirectory, bool preferDub)
    {
        _core = core;
        _surface = surface;
        _overlay = overlay;
        _configDirectory = configDirectory;
        _preferDub = preferDub;
        _dispatcher = surface.DispatcherQueue;
        _player.CommandManager.IsEnabled = false;
        _player.TimelineController = _timeline;
        _surface.SetMediaPlayer(_player);
        _player.MediaFailed += OnMediaFailed;
        _player.PlaybackSession.SeekCompleted += OnSeekCompleted;
        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        _timeline.Ended += OnEnded;
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += OnTick;
    }

    public event Action? Ready;
    public event Action? Ended;
    public event Action<string>? Error;
    public event Action<double, double>? PositionChanged;
    public event Action<bool>? PauseChanged;
    public event Action<bool>? BufferingChanged;
    public bool IsInitialized { get; private set; }
    public bool HasSubs => _assets.Tracks.Count > 0 || (_item?.TimedMetadataTracks.Count ?? 0) > 0;
    public bool IsPaused => _paused;
    public double Position => _seekTarget ?? _timeline.Position.TotalSeconds;
    public bool IsSeeking => _seekTarget.HasValue;
    public double Duration => _player.PlaybackSession.NaturalDuration.TotalSeconds;
    public double Volume => (_audio ?? _player).Volume * 100;
    public bool IsMuted => (_audio ?? _player).IsMuted;
    public long SubTrack => _subtitle;
    public long AudioTrack => (_audioItem ?? _item)?.AudioTracks.SelectedIndex + 1 ?? 0;
    public bool IsAdaptive => _adaptive?.AvailableBitrates.Any(b => b > 0) == true;
    public bool IsAutomaticQuality => _adaptive is not null && _adaptive.DesiredMinBitrate is null && _adaptive.DesiredMaxBitrate is null;
    public void SetAutomaticQuality()
    {
        if (_adaptive is null) return;
        _adaptive.DesiredMinBitrate = null;
        _adaptive.DesiredMaxBitrate = null;
    }

    public void Load(PlaybackIntentDto intent, IReadOnlyList<string> subPaths) => _ = LoadAsync(intent, subPaths);

    public Task LoadAsync(PlaybackIntentDto intent, IReadOnlyList<string> subPaths, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loadTask = LoadCoreAsync(intent, subPaths, ct);
    }

    private async Task LoadCoreAsync(PlaybackIntentDto intent, IReadOnlyList<string> subPaths, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        ct = linked.Token;
        try
        {
            var source = intent.VideoSrc ?? throw new InvalidOperationException("No video source.");
            await _assets.PrepareAsync(intent, subPaths, _configDirectory, ct);
            ct.ThrowIfCancellationRequested();
            if (_disposed) return;
            _subtitles = new AssRenderer(_overlay, _assets.FontsDirectory);
            _item = new MediaPlaybackItem(await CreateSourceAsync(source, true, ct));
            if (_disposed) return;
            var videoOpened = WaitForOpenAsync(_player, ct);
            _player.Source = _item;
            Task audioOpened = Task.CompletedTask;
            if (!string.IsNullOrWhiteSpace(intent.AudioSrc) && intent.AudioSrc != source)
            {
                _audio = new MediaPlayer();
                _audio.CommandManager.IsEnabled = false;
                _audio.TimelineController = _timeline;
                _audio.MediaFailed += OnMediaFailed;
                _audioItem = new MediaPlaybackItem(await CreateSourceAsync(intent.AudioSrc, false, ct));
                if (_disposed) return;
                audioOpened = WaitForOpenAsync(_audio, ct);
                _audio.Source = _audioItem;
                _player.IsMuted = true;
            }
            await Task.WhenAll(videoOpened, audioOpened);
            ct.ThrowIfCancellationRequested();
            if (_disposed) return;
            for (uint i = 0; i < _item.TimedMetadataTracks.Count; i++)
                _item.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled);
            var selection = await _core.CallAsync<TrackSelectionDto>("selectTracks", new
            { audio = ReadAudioTracks(), subtitles = ReadSubtitleTracks(), preferDub = _preferDub }, ct);
            if (_disposed) return;
            if (selection.Audio is > 0) SetAudioTrack(selection.Audio.Value);
            SetSubTrack(selection.Subtitle ?? 0);
            IsInitialized = true;
            _timer.Start();
            CompositionTarget.Rendering += OnRendering;
            // Start before Ready: core resume can then seek without Start resetting it.
            _timeline.Start();
            _paused = false;
            Ready?.Invoke();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Anibel.App.Services.Diag.Log($"Native playback load failed: {ex}");
            if (!_disposed) Error?.Invoke(ex.Message);
            throw;
        }
    }

    private async Task<MediaSource> CreateSourceAsync(string path, bool video, CancellationToken ct)
    {
        MediaSource source;
        if (System.IO.File.Exists(path))
            source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(StoragePath(path)).AsTask(ct));
        else
        {
            var uri = new Uri(path);
            var extension = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".mpd" or ".m3u8")
            {
                // Use the same manifest for playback and resolution labels, including redirects.
                using var response = await ManifestClient.GetAsync(uri, ct);
                response.EnsureSuccessStatusCode();
                var manifest = await response.Content.ReadAsByteArrayAsync(ct);
                var data = (manifest, response.RequestMessage?.RequestUri ?? uri,
                    extension == ".mpd" ? "application/dash+xml" : "application/vnd.apple.mpegurl");
                var adaptive = await OpenAdaptiveAsync(data, ct);
                if (video)
                {
                    _manifest = data;
                    _adaptive = adaptive;
                    _videoSizes = AdaptiveVideoQualities.Parse(System.Text.Encoding.UTF8.GetString(manifest));
                    var highest = ReadVideoTracks().Where(t => t.Bitrate > 0)
                        .OrderByDescending(t => t.Height).ThenByDescending(t => t.Width).ThenByDescending(t => t.Bitrate).FirstOrDefault();
                    if (highest is not null) ApplyQuality(adaptive, checked((uint)highest.Id));
                }
                source = MediaSource.CreateFromAdaptiveMediaSource(adaptive);
            }
            else source = MediaSource.CreateFromUri(uri);
        }
        if (_disposed) { source.Dispose(); ct.ThrowIfCancellationRequested(); throw new ObjectDisposedException(nameof(WindowsMediaEngine)); }
        _sources.Add(source);
        return source;
    }

    private static async Task<AdaptiveMediaSource> OpenAdaptiveAsync((byte[] Data, Uri Uri, string ContentType) manifest, CancellationToken ct)
    {
        using var memory = new System.IO.MemoryStream(manifest.Data, writable: false);
        using var input = memory.AsInputStream();
        var result = await AdaptiveMediaSource.CreateFromStreamAsync(input, manifest.Uri, manifest.ContentType).AsTask(ct);
        if (result.Status != AdaptiveMediaSourceCreationStatus.Success)
            throw new InvalidOperationException($"Windows could not open this adaptive stream ({result.Status}).");
        return result.MediaSource;
    }

    internal static string StoragePath(string path)
    {
        // Rust canonicalization returns a verbatim path. StorageFile requires DOS/UNC syntax.
        var full = System.IO.Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + full[8..];
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) && full.Length > 6 && full[5] == ':') return full[4..];
        return full;
    }

    private static async Task WaitForOpenAsync(MediaPlayer player, CancellationToken ct)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Opened(MediaPlayer _, object args) => completion.TrySetResult();
        void Failed(MediaPlayer _, MediaPlayerFailedEventArgs args) => completion.TrySetException(new InvalidOperationException(MediaError(args), args.ExtendedErrorCode));
        player.MediaOpened += Opened;
        player.MediaFailed += Failed;
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), ct); }
        finally { player.MediaOpened -= Opened; player.MediaFailed -= Failed; }
    }

    public AudioTrackInfo[] ReadAudioTracks()
    {
        var tracks = (_audioItem ?? _item)?.AudioTracks;
        return tracks is null ? [] : tracks.Select((t, i) => new AudioTrackInfo(i + 1, t.Language, t.Label)).ToArray();
    }
    public SubtitleTrackInfo[] ReadSubtitleTracks()
    {
        var tracks = new List<SubtitleTrackInfo>(_assets.Tracks);
        if (_item is not null)
            for (var i = 0; i < _item.TimedMetadataTracks.Count; i++)
            {
                var track = _item.TimedMetadataTracks[i];
                // Extracted MKV text tracks are rendered by libass, not twice by Windows.
                if (track.TimedMetadataKind == TimedMetadataKind.ImageSubtitle ||
                    (_assets.Tracks.Count == 0 && track.TimedMetadataKind == TimedMetadataKind.Subtitle))
                    tracks.Add(new(_assets.Tracks.Count + i + 1, track.Label, track.Language, ""));
            }
        return tracks.ToArray();
    }
    public VideoTrackInfo[] ReadVideoTracks()
    {
        if (IsAdaptive)
            return _adaptive!.AvailableBitrates.Where(b => b > 0).Distinct().Select(b =>
            {
                var size = _videoSizes.GetValueOrDefault(b);
                return new VideoTrackInfo(b, size.Width, size.Height, b, "", false,
                    _adaptive.DesiredMinBitrate == b && _adaptive.DesiredMaxBitrate == b);
            }).ToArray();
        return [new(1, _player.PlaybackSession.NaturalVideoWidth, _player.PlaybackSession.NaturalVideoHeight, 0, "", false, true)];
    }
    public async Task SetVideoTrackAsync(long id)
    {
        if (_adaptive is null || _manifest is not { } manifest) return;
        var bitrate = checked((uint)id);
        if (!_adaptive.AvailableBitrates.Contains(bitrate)) throw new ArgumentOutOfRangeException(nameof(id));
        var ct = _lifetime.Token;
        await _qualitySwitch.WaitAsync(ct);
        try
        {
            if (_adaptive.DesiredMinBitrate == bitrate && _adaptive.DesiredMaxBitrate == bitrate) return;
            // Bounds only affect future downloads. A fresh source drops old-quality buffered video.
            var adaptive = await OpenAdaptiveAsync(manifest, ct);
            ApplyQuality(adaptive, bitrate);
            var source = MediaSource.CreateFromAdaptiveMediaSource(adaptive);
            if (_disposed) { source.Dispose(); return; }
            _sources.Add(source);
            var position = Position;
            var audio = AudioTrack;
            var subtitle = SubTrack;
            var previous = _item!.Source;
            _timeline.Pause();
            var opened = WaitForOpenAsync(_player, ct);
            _item = new MediaPlaybackItem(source);
            _adaptive = adaptive;
            _player.Source = _item;
            _sources.Remove(previous);
            previous.Dispose();
            await opened;
            ct.ThrowIfCancellationRequested();
            SetAudioTrack(audio);
            SetSubTrack(subtitle);
            _timeline.Position = TimeSpan.FromSeconds(position);
        }
        finally
        {
            if (!_disposed && !_paused) _timeline.Resume();
            _qualitySwitch.Release();
        }
    }

    private static void ApplyQuality(AdaptiveMediaSource source, uint bitrate)
    {
        source.InitialBitrate = bitrate;
        source.DesiredMinBitrate = null;
        source.DesiredMaxBitrate = bitrate;
        source.DesiredMinBitrate = bitrate;
    }
    public void SetAudioTrack(long id)
    {
        var tracks = (_audioItem ?? _item)?.AudioTracks;
        if (tracks is not null && id > 0 && id <= tracks.Count) tracks.SelectedIndex = (int)id - 1;
    }
    public void SetSubTrack(long id)
    {
        var track = ReadSubtitleTracks().FirstOrDefault(t => t.Id == id);
        if (id != 0 && track.Id != id) throw new ArgumentOutOfRangeException(nameof(id));
        _subtitles?.Load(id == 0 || string.IsNullOrEmpty(track.FileName) ? null : track.FileName);
        if (_item is not null)
            for (uint i = 0; i < _item.TimedMetadataTracks.Count; i++)
                _item.TimedMetadataTracks.SetPresentationMode(i, id == _assets.Tracks.Count + i + 1
                    ? TimedMetadataTrackPresentationMode.PlatformPresented : TimedMetadataTrackPresentationMode.Disabled);
        _subtitle = id;
    }
    public void TogglePause()
    {
        if (_paused) _timeline.Resume(); else _timeline.Pause();
        _paused = !_paused;
        PauseChanged?.Invoke(_paused);
    }
    public void Seek(double seconds)
    {
        if (_disposed || !IsInitialized || !double.IsFinite(seconds)) return;
        var target = Math.Clamp(seconds, 0, Math.Max(Duration, 0));
        if (!_seekTarget.HasValue && Math.Abs(_timeline.Position.TotalSeconds - target) < 0.05) return;
        _seekTarget = target;
        PositionChanged?.Invoke(Position, Duration);
        BufferingChanged?.Invoke(true);
        // Let the control paint first. While seeking, keep only the newest drag position.
        OnUi(StartPendingSeek);
    }

    private void StartPendingSeek()
    {
        if (_activeSeek.HasValue || _seekTarget is not { } target) return;
        _activeSeek = target;
        _timeline.Position = TimeSpan.FromSeconds(target);
    }

    private void OnSeekCompleted(MediaPlaybackSession sender, object args) => OnUi(() =>
    {
        if (_activeSeek is not { } completed) return;
        _activeSeek = null;
        if (_seekTarget == completed) _seekTarget = null;
        else StartPendingSeek();
        PositionChanged?.Invoke(Position, Duration);
        NotifyBuffering();
    });

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args) => OnUi(() =>
    {
        if (IsInitialized) NotifyBuffering();
    });
    private void NotifyBuffering() => BufferingChanged?.Invoke(IsSeeking ||
        _player.PlaybackSession.PlaybackState == MediaPlaybackState.Buffering);
    public void SetVolume(double volume) { if (double.IsFinite(volume)) (_audio ?? _player).Volume = Math.Clamp(volume / 100, 0, 1); }
    public void SetMute(bool mute) => (_audio ?? _player).IsMuted = mute;
    public void Resize(uint width, uint height) => InvalidateSurface();
    public void InvalidateSurface() { if (!_disposed && IsInitialized) DrawSubtitles(); }
    private void OnTick(DispatcherQueueTimer sender, object args) => PositionChanged?.Invoke(Position, Duration);
    private void OnRendering(object? sender, object args) { if (!_disposed && IsInitialized) DrawSubtitles(); }
    private void DrawSubtitles()
    {
        try { _subtitles?.Render(Position, _surface.ActualWidth, _surface.ActualHeight, _player.PlaybackSession.NaturalVideoWidth, _player.PlaybackSession.NaturalVideoHeight, _overlay.XamlRoot?.RasterizationScale ?? 1); }
        catch (Exception ex) { _subtitles?.Load(null); Error?.Invoke($"Subtitle rendering failed: {ex.Message}"); }
    }
    private static string MediaError(MediaPlayerFailedEventArgs args)
        => $"{args.Error}: {args.ErrorMessage} (0x{args.ExtendedErrorCode?.HResult ?? 0:X8})";
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var message = MediaError(args);
        Anibel.App.Services.Diag.Log($"Native playback failed: {message}");
        OnUi(() => { _seekTarget = _activeSeek = null; BufferingChanged?.Invoke(false); Error?.Invoke(message); });
    }
    private void OnEnded(MediaTimelineController sender, object args) => OnUi(() =>
    {
        _paused = true;
        PauseChanged?.Invoke(true);
        Ended?.Invoke();
    });
    private void OnUi(Action action) => _dispatcher.TryEnqueue(() => { if (!_disposed) action(); });
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        IsInitialized = false;
        _timer.Stop();
        _timer.Tick -= OnTick;
        CompositionTarget.Rendering -= OnRendering;
        _timeline.Pause();
        _timeline.Ended -= OnEnded;
        _surface.SetMediaPlayer(null);
        _player.MediaFailed -= OnMediaFailed;
        _player.PlaybackSession.SeekCompleted -= OnSeekCompleted;
        _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        if (_audio is not null) { _audio.MediaFailed -= OnMediaFailed; _audio.Dispose(); }
        _player.Dispose();
        foreach (var source in _sources) source.Dispose();
        _sources.Clear();
        _subtitles?.Dispose();
        _ = DisposeAssetsAfterLoadAsync();
    }

    private async Task DisposeAssetsAfterLoadAsync()
    {
        try { await _loadTask; } catch { /* Load failures have already been reported. */ }
        _assets.Dispose();
        _lifetime.Dispose();
    }
}
