using Anibel.App.Core;
using FFmpegInteropX;
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
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly MediaPlayer _player = new();
    private MediaTimelineController _timeline = new();
    private readonly SubtitlePresentation _subtitles;
    private readonly string _configDirectory;
    private readonly bool _preferDub;
    private readonly List<MediaSource> _sources = [];
    private MediaPlayer? _audio;
    private SoftwareVideoSource? _softwareSource;
    private string? _videoPath;
    private Task _recoveryTask = Task.CompletedTask;
    internal bool IsSoftwareDecoding => _softwareSource is not null;
    private MediaPlaybackItem? _item, _audioItem;
    private AdaptiveMediaSource? _adaptive;
    private (byte[] Data, Uri Uri, string ContentType)? _manifest;
    private readonly SemaphoreSlim _qualitySwitch = new(1, 1);
    private Dictionary<uint, (uint Width, uint Height)> _videoSizes = [];
    private bool _disposed;
    // Timeline.State changes asynchronously. Toggle the user's intent, not its delayed acknowledgement.
    private bool _paused = true;
    private double? _seekTarget;
    private double? _activeSeekTarget;
    private readonly HashSet<MediaPlaybackSession> _seekingSessions = [];
    private double _duration;
    private bool _changingSource;
    private readonly HashSet<MediaPlaybackSession> _bufferingSessions = [];
    public Task CleanupCompleted { get; private set; } = Task.CompletedTask;
    private long _subtitle;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _loadTask = Task.CompletedTask;
    private readonly HashSet<Task> _qualityTasks = [];

    public WindowsMediaEngine(ICoreClient core, MediaPlayerElement surface, Image overlay, string configDirectory, bool preferDub)
    {
        _core = core;
        _surface = surface;
        _subtitles = new SubtitlePresentation(overlay);
        _configDirectory = configDirectory;
        _preferDub = preferDub;
        _dispatcher = surface.DispatcherQueue;
        _player.CommandManager.IsEnabled = false;
        _player.TimelineController = _timeline;
        _surface.SetMediaPlayer(_player);
        _player.MediaFailed += OnMediaFailed;
        ObserveBuffering(_player.PlaybackSession);
        _timeline.Ended += OnEnded;
        _timeline.PositionChanged += OnTimelinePositionChanged;
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
    public bool HasSubs => _subtitles.Tracks.Count > 0 || (_item?.TimedMetadataTracks.Count ?? 0) > 0;
    public bool IsPaused => _paused;
    public double Position => _seekTarget ?? ActualPosition;
    private double ActualPosition => _timeline.Position.TotalSeconds;
    public bool IsBuffering => _changingSource || IsSeeking || _bufferingSessions.Count > 0
        || _player.PlaybackSession.PlaybackState is MediaPlaybackState.Opening or MediaPlaybackState.Buffering
        || _audio?.PlaybackSession.PlaybackState is MediaPlaybackState.Opening or MediaPlaybackState.Buffering;
    public bool IsSeeking => _seekTarget.HasValue;
    internal bool IsChangingSource => _changingSource;
    public double Duration
    {
        get
        {
            // Keep the timeline range while a failed or replaced source has no metadata.
            if (_softwareSource is not null) return _softwareSource.Duration;
            try
            {
                var duration = _player.PlaybackSession.NaturalDuration.TotalSeconds;
                if (duration > 0) _duration = duration;
            }
            catch (System.Runtime.InteropServices.COMException) { /* MediaFailed owns the error. */ }
            return _duration;
        }
    }
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
            _videoPath = source;
            await _subtitles.PrepareAsync(intent, subPaths, _configDirectory, ct);
            ct.ThrowIfCancellationRequested();
            if (_disposed) return;
            var videoOpened = OpenVideoAsync(source, ct);
            Task audioOpened = Task.CompletedTask;
            if (!string.IsNullOrWhiteSpace(intent.AudioSrc) && intent.AudioSrc != source)
            {
                _audio = new MediaPlayer();
                _audio.CommandManager.IsEnabled = false;
                _audio.TimelineController = _timeline;
                _audio.MediaFailed += OnMediaFailed;
                ObserveBuffering(_audio.PlaybackSession);
                _audioItem = new MediaPlaybackItem(await CreateSourceAsync(intent.AudioSrc, false, ct));
                if (_disposed) return;
                audioOpened = WaitForOpenAsync(_audio, ct);
                _audio.Source = _audioItem;
                _player.IsMuted = true;
            }
            await Task.WhenAll(videoOpened, audioOpened);
            ct.ThrowIfCancellationRequested();
            if (_disposed) return;
            var item = _item ?? throw new InvalidOperationException("Video source did not open.");
            for (uint i = 0; i < item.TimedMetadataTracks.Count; i++)
                item.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled);
            var selection = await _core.CallAsync<TrackSelectionDto>(CoreCommand.SelectTracks, new
            { audio = ReadAudioTracks(), subtitles = ReadSubtitleTracks(), preferDub = _preferDub }, ct);
            if (_disposed) return;
            if (selection.Audio is > 0) SetAudioTrack(selection.Audio.Value);
            SetSubTrack(selection.Subtitle ?? 0);
            IsInitialized = true;
            _timer.Start();
            CompositionTarget.Rendering += OnRendering;
            // Start before Ready: core resume can then seek without Start resetting it.
            _timeline.Position = TimeSpan.Zero;
            _paused = false;
            ApplyTimelineState();
            Ready?.Invoke();
            NotifyBuffering();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Anibel.App.Services.Diag.Log($"Native playback load failed: {ex}");
            if (!_disposed) Error?.Invoke(ex.Message);
            throw;
        }
    }

    private async Task OpenVideoAsync(string path, CancellationToken ct)
    {
        var nativeSource = await CreateSourceAsync(path, true, ct);
        if (_manifest is { } manifest && _videoSizes.Count > 0)
        {
            var selected = _videoSizes.OrderByDescending(t => t.Value.Height)
                .ThenByDescending(t => t.Value.Width).ThenByDescending(t => t.Key).First().Key;
            var rendition = AdaptiveVideoQualities.Select(System.Text.Encoding.UTF8.GetString(manifest.Data), manifest.Uri, selected);
            if (AdaptiveVideoQualities.RequiresSoftwareDecoder(rendition))
            {
                _sources.Remove(nativeSource);
                nativeSource.Dispose();
                await OpenSoftwareVideoAsync(path, ct);
                return;
            }
        }
        _item = new MediaPlaybackItem(nativeSource);
        var opened = WaitForOpenAsync(_player, ct);
        _player.Source = _item;
        try { await opened; }
        catch (PlaybackOpenException ex) when (CanDecodeInSoftware(ex.Error))
        {
            await OpenSoftwareVideoAsync(path, ct);
        }
    }

    private static bool CanDecodeInSoftware(MediaPlayerError error)
        => error is MediaPlayerError.DecodingError or MediaPlayerError.SourceNotSupported;

    private async Task OpenSoftwareVideoAsync(string path, CancellationToken ct, uint? bitrate = null)
    {
        var source = await SoftwareVideoSource.OpenAsync(path, _manifest, _videoSizes, bitrate, ct);
        if (_disposed || ct.IsCancellationRequested) {
            source.Dispose();
            ct.ThrowIfCancellationRequested();
            throw new ObjectDisposedException(nameof(WindowsMediaEngine));
        }
        var decoder = source.Decoder;
        _activeSeekTarget = null;
        _seekingSessions.Clear();
        _player.Source = null;
        if (_item is not null)
        {
            _sources.Remove(_item.Source);
            _item.Source.Dispose();
        }
        _softwareSource?.Dispose();
        _softwareSource = source;
        _adaptive = null;
        _bufferingSessions.Remove(_player.PlaybackSession);
        _item = decoder.CreateMediaPlaybackItem();
        decoder.PlaybackSession = _player.PlaybackSession;
        var opened = WaitForOpenAsync(_player, ct);
        _player.Source = _item;
        await opened;
        Anibel.App.Services.Diag.Log("Software decoder: player ready");

    }

    private async Task RecoverVideoAsync()
    {
        _changingSource = true;
        NotifyBuffering();
        try
        {
            await _qualitySwitch.WaitAsync(_lifetime.Token);
            try
            {
                _changingSource = true;
                _seekTarget = Position;
                var audio = AudioTrack;
                var subtitle = SubTrack;
                await OpenSoftwareVideoAsync(_videoPath!, _lifetime.Token);
                if (_disposed) return;
                SetAudioTrack(audio);
                SetSubTrack(subtitle);
                _changingSource = false;
                StartPendingSeek();
            }
            finally { _qualitySwitch.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) ReportFailure(ex.Message); }
        finally
        {
            _changingSource = false;
            if (!_disposed && IsInitialized) NotifyBuffering();
        }
    }

    private sealed class PlaybackOpenException(MediaPlayerFailedEventArgs args)
        : InvalidOperationException(MediaError(args), args.ExtendedErrorCode)
    {
        public MediaPlayerError Error { get; } = args.Error;
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
        void Failed(MediaPlayer _, MediaPlayerFailedEventArgs args) => completion.TrySetException(new PlaybackOpenException(args));
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
        var tracks = new List<SubtitleTrackInfo>(_subtitles.Tracks);
        if (_item is not null)
            for (var i = 0; i < _item.TimedMetadataTracks.Count; i++)
            {
                var track = _item.TimedMetadataTracks[i];
                // Extracted MKV text tracks are rendered by libass, not twice by Windows.
                if (track.TimedMetadataKind == TimedMetadataKind.ImageSubtitle ||
                    (_subtitles.Tracks.Count == 0 && track.TimedMetadataKind == TimedMetadataKind.Subtitle))
                    tracks.Add(new(_subtitles.Tracks.Count + i + 1, track.Label, track.Language, ""));
            }
        return tracks.ToArray();
    }
    public VideoTrackInfo[] ReadVideoTracks()
    {
        if (_softwareSource is not null && _manifest is not null && _videoSizes.Count > 0)
        {
            var current = _softwareSource.VideoTracks.ElementAtOrDefault(_item?.VideoTracks.SelectedIndex ?? 0);
            return _videoSizes.Select(t => new VideoTrackInfo(t.Key, t.Value.Width, t.Value.Height, t.Key, "", false,
                t.Value.Width == current?.Width && t.Value.Height == current?.Height)).ToArray();
        }
        if (_softwareSource is not null)
            return _softwareSource.VideoTracks.Select((track, i) => track with
                { Selected = _item?.VideoTracks.SelectedIndex == i }).ToArray();
        if (IsAdaptive)
            return _adaptive!.AvailableBitrates.Where(b => b > 0).Distinct().Select(b =>
            {
                var size = _videoSizes.GetValueOrDefault(b);
                return new VideoTrackInfo(b, size.Width, size.Height, b, "", false,
                    _adaptive.DesiredMinBitrate == b && _adaptive.DesiredMaxBitrate == b);
            }).ToArray();
        return [new(1, _player.PlaybackSession.NaturalVideoWidth, _player.PlaybackSession.NaturalVideoHeight, 0, "", false, true)];
    }
    public Task SetVideoTrackAsync(long id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var task = ChangeVideoTrackAsync(id);
        _qualityTasks.Add(task);
        _ = RemoveQualityTaskAsync(task);
        return task;
    }
    private async Task RemoveQualityTaskAsync(Task task)
    {
        try { await task; }
        catch { /* The menu owns error handling. */ }
        finally { _qualityTasks.Remove(task); }
    }
    private async Task ChangeVideoTrackAsync(long id)
    {
        if (_softwareSource is not null)
        {
            if (_manifest is null || _videoSizes.Count == 0)
            {
                if (id < 1 || id > _item!.VideoTracks.Count) throw new ArgumentOutOfRangeException(nameof(id));
                _seekTarget = Position;
                _item.VideoTracks.SelectedIndex = checked((int)id - 1);
                StartPendingSeek();
                return;
            }
            if (!_videoSizes.ContainsKey(checked((uint)id))) throw new ArgumentOutOfRangeException(nameof(id));
            await _qualitySwitch.WaitAsync(_lifetime.Token);
            try
            {
                if (ReadVideoTracks().Any(t => t.Id == id && t.Selected)) return;
                _changingSource = true;
                _seekTarget = Position;
                var audio = AudioTrack;
                var subtitle = SubTrack;
                NotifyBuffering();
                await OpenSoftwareVideoAsync(_videoPath!, _lifetime.Token, checked((uint)id));
                _lifetime.Token.ThrowIfCancellationRequested();
                SetAudioTrack(audio);
                SetSubTrack(subtitle);
            }
            finally
            {
                _changingSource = false;
                if (!_disposed) { StartPendingSeek(retainClock: true); NotifyBuffering(); }
                _qualitySwitch.Release();
            }
            return;
        }
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
            _changingSource = true;
            _seekTarget = position;
            NotifyBuffering();
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
            _changingSource = false;
            StartPendingSeek(retainClock: true);
        }
        finally
        {
            _changingSource = false;
            if (!_disposed)
            {
                StartPendingSeek(retainClock: true);
                NotifyBuffering();
            }
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
        _subtitles.Load(id == 0 || string.IsNullOrEmpty(track.FileName) ? null : track.FileName);
        if (_item is not null)
            for (uint i = 0; i < _item.TimedMetadataTracks.Count; i++)
                _item.TimedMetadataTracks.SetPresentationMode(i, id == _subtitles.Tracks.Count + i + 1
                    ? TimedMetadataTrackPresentationMode.PlatformPresented : TimedMetadataTrackPresentationMode.Disabled);
        _subtitle = id;
    }
    public void TogglePause()
    {
        _paused = !_paused;
        ApplyTimelineState();
        PauseChanged?.Invoke(_paused);
    }
    public void Seek(double seconds)
    {
        if (_disposed || !IsInitialized || !double.IsFinite(seconds)) return;
        var target = Math.Clamp(seconds, 0, Math.Max(Duration, 0));
        if (!_seekTarget.HasValue && Math.Abs(_timeline.Position.TotalSeconds - target) < 0.05) return;
        _seekTarget = target;
        PositionChanged?.Invoke(Position, Duration);
        NotifyBuffering();
        // Let the control paint first. While seeking, keep only the newest drag position.
        OnUi(() => StartPendingSeek());
    }

    private void StartPendingSeek(bool retainClock = false)
    {
        if (_changingSource || _activeSeekTarget.HasValue || _seekTarget is not { } target) return;
        // Quality replacement already opens at the shared clock. Do not create a
        // second seek when the requested position is unchanged: paused Windows
        // playback can omit SeekCompleted for that no-op and leave us waiting.
        // Decoder recovery must still replace the failed source's clock.
        if (retainClock && Math.Abs(target - ActualPosition) < .05)
        {
            _seekTarget = null;
            PositionChanged?.Invoke(Position, Duration);
            NotifyBuffering();
            return;
        }
        // A fresh clock preserves the latest target even when a paused HLS player
        // has not acknowledged its previous seek. Both players share this clock.
        // MediaStreamSource acknowledges exact seeks. Windows adaptive sources may
        // omit SeekCompleted while paused; their shared timeline owns that transition.
        if (_softwareSource is not null)
        {
            _activeSeekTarget = target;
            _seekingSessions.Add(_player.PlaybackSession);
            if (_audio is not null) _seekingSessions.Add(_audio.PlaybackSession);
        }
        var next = new MediaTimelineController { Position = TimeSpan.FromSeconds(target) };
        _timeline.Ended -= OnEnded;
        _timeline.PositionChanged -= OnTimelinePositionChanged;
        _timeline.Pause();
        _timeline = next;
        _timeline.Ended += OnEnded;
        _timeline.PositionChanged += OnTimelinePositionChanged;
        _player.TimelineController = next;
        if (_audio is not null) _audio.TimelineController = next;
        if (_softwareSource is null) _seekTarget = null;
        PositionChanged?.Invoke(Position, Duration);
        NotifyBuffering();
    }

    private void OnSeekCompleted(MediaPlaybackSession sender, object args) => OnUi(() =>
    {
        if (!_seekingSessions.Remove(sender) || _seekingSessions.Count > 0) return;
        var completed = _activeSeekTarget;
        _activeSeekTarget = null;
        if (_seekTarget == completed) _seekTarget = null;
        else StartPendingSeek();
        PositionChanged?.Invoke(Position, Duration);
        NotifyBuffering();
    });

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args) => OnUi(() =>
    {
        if (IsInitialized) NotifyBuffering();
    });
    private void ObserveBuffering(MediaPlaybackSession session)
    {
        session.SeekCompleted += OnSeekCompleted;
        session.PlaybackStateChanged += OnPlaybackStateChanged;
        session.BufferingStarted += OnBufferingStarted;
        session.BufferingEnded += OnBufferingEnded;
    }
    private void StopObservingBuffering(MediaPlaybackSession session)
    {
        session.SeekCompleted -= OnSeekCompleted;
        session.PlaybackStateChanged -= OnPlaybackStateChanged;
        session.BufferingStarted -= OnBufferingStarted;
        session.BufferingEnded -= OnBufferingEnded;
        _bufferingSessions.Remove(session);
    }
    private void OnBufferingStarted(MediaPlaybackSession sender, object args) => OnUi(() =>
    {
        _bufferingSessions.Add(sender);
        if (IsInitialized) NotifyBuffering();
    });
    private void OnBufferingEnded(MediaPlaybackSession sender, object args) => OnUi(() =>
    {
        _bufferingSessions.Remove(sender);
        if (IsInitialized) NotifyBuffering();
    });
    private void ApplyTimelineState()
    {
        // Both players must wait at the same clock position. User pause intent
        // stays separate so ending a buffer cannot override a pause click.
        if (_paused || IsBuffering) _timeline.Pause();
        else _timeline.Resume();
    }
    private void NotifyBuffering()
    {
        ApplyTimelineState();
        BufferingChanged?.Invoke(IsBuffering);
    }
    public void SetVolume(double volume) { if (double.IsFinite(volume)) (_audio ?? _player).Volume = Math.Clamp(volume / 100, 0, 1); }
    public void SetMute(bool mute) => (_audio ?? _player).IsMuted = mute;
    public void Resize(uint width, uint height) => InvalidateSurface();
    public void InvalidateSurface() { if (!_disposed && IsInitialized) DrawSubtitles(); }
    private void OnTimelinePositionChanged(MediaTimelineController sender, object args) => OnUi(UpdatePosition);
    private void OnTick(DispatcherQueueTimer sender, object args) => UpdatePosition();
    private void UpdatePosition()
    {
        PositionChanged?.Invoke(Position, Duration);
    }
    private void OnRendering(object? sender, object args) { if (!_disposed && IsInitialized) DrawSubtitles(); }
    private void DrawSubtitles()
    {
        if (_changingSource) return;
        uint width, height;
        try
        {
            var softwareTrack = _softwareSource?.VideoTracks.ElementAtOrDefault(_item?.VideoTracks.SelectedIndex ?? 0);
            width = softwareTrack is not null ? checked((uint)softwareTrack.Width) : _player.PlaybackSession.NaturalVideoWidth;
            height = softwareTrack is not null ? checked((uint)softwareTrack.Height) : _player.PlaybackSession.NaturalVideoHeight;
        }
        catch (System.Runtime.InteropServices.COMException) { return; } // MediaFailed owns decoder errors.
        try { _subtitles.Render(ActualPosition, _surface.ActualWidth, _surface.ActualHeight, width, height); }
        catch (Exception ex) { _subtitles.Load(null); Error?.Invoke($"Subtitle rendering failed: {ex.Message}"); }
    }
    private static string MediaError(MediaPlayerFailedEventArgs args)
        => $"{args.Error}: {args.ErrorMessage} (0x{args.ExtendedErrorCode?.HResult ?? 0:X8})";
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var message = MediaError(args);
        Anibel.App.Services.Diag.Log($"Native {(sender.Equals(_player) ? "video" : "audio")} playback failed: {message}; initialized={IsInitialized}, switching={_changingSource}");
        var failedItem = sender.Equals(_player) ? _item : _audioItem;
        OnUi(() => _ = HandleMediaFailureAsync(sender, failedItem, args.Error, message));
    }
    private async Task HandleMediaFailureAsync(MediaPlayer sender, MediaPlaybackItem? failedItem,
        MediaPlayerError error, string message)
    {
        // MediaOpened can precede a decoder failure while track selection is still loading.
        // Wait for that load, then ignore errors from any source it has already replaced.
        try { await _loadTask; } catch { return; }
        if (_disposed || !IsInitialized || _changingSource || !_recoveryTask.IsCompleted
            || !ReferenceEquals(failedItem, sender.Equals(_player) ? _item : _audioItem)) return;
        if (sender.Equals(_player) && _softwareSource is null
            && _recoveryTask.IsCompleted && CanDecodeInSoftware(error))
            await (_recoveryTask = RecoverVideoAsync());
        else ReportFailure(message);
    }
    private void ReportFailure(string message)
    {
        IsInitialized = false;
        _paused = true;
        _seekTarget = null;
        _activeSeekTarget = null;
        _seekingSessions.Clear();
        _changingSource = false;
        _bufferingSessions.Clear();
        _timeline.Pause();
        BufferingChanged?.Invoke(false);
        Error?.Invoke(message);
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
        _timeline.PositionChanged -= OnTimelinePositionChanged;
        _surface.SetMediaPlayer(null);
        _player.MediaFailed -= OnMediaFailed;
        StopObservingBuffering(_player.PlaybackSession);
        if (_audio is not null) { StopObservingBuffering(_audio.PlaybackSession); _audio.MediaFailed -= OnMediaFailed; _audio.Dispose(); }
        _player.Dispose();
        foreach (var source in _sources) source.Dispose();
        _sources.Clear();
        _softwareSource?.Dispose();
        _softwareSource = null;
        _subtitles.StopRendering();
        CleanupCompleted = DisposeAssetsAfterLoadAsync();
    }

    private async Task DisposeAssetsAfterLoadAsync()
    {
        try { await _loadTask; } catch { /* Load failures have already been reported. */ }
        try { await Task.WhenAll(_qualityTasks.ToArray()); } catch { /* Cancelled during close. */ }
        try { await _recoveryTask; } catch { /* Recovery failure was reported. */ }
        _subtitles.Dispose();
        _lifetime.Dispose();
    }
}
