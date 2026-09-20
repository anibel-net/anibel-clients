using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Anibel.App.Core;
using Anibel.App.Playback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PlaybackSmoke;

// Use the shipped Rust core, with test-only storage and no account credentials.
public class SelectionProxy : DispatchProxy
{
    internal long Handle;
    private long _request;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name == "CallAsync")
            return typeof(SelectionProxy).GetMethod(nameof(Call))!.MakeGenericMethod(method.GetGenericArguments())
                .Invoke(this, [args![0], args[1]]);
        throw new NotSupportedException(method?.Name);
    }
    public async Task<T> Call<T>(string op, object? args)
    {
        var request = JsonSerializer.Serialize(new { id = Interlocked.Increment(ref _request), op, args }, Json);
        var raw = await Task.Run(() =>
        {
            var ptr = AnibelCoreNative.anibel_core_call(Handle, request);
            try { return Marshal.PtrToStringUTF8(ptr)!; }
            finally { AnibelCoreNative.anibel_core_free(ptr); }
        });
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (!root.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(root.GetProperty("error").ToString());
        return root.GetProperty("value").Deserialize<T>(Json)!;
    }
}

internal sealed class DecoderTestLog : FFmpegInteropX.ILogProvider
{
    public void Log(FFmpegInteropX.LogLevel level, string message)
        => Anibel.App.Services.Diag.Log("FFmpeg: " + System.Text.RegularExpressions.Regex.Replace(message, @"https?://[^\s'""<>]+", "[URL]"));
}

internal sealed class RestartTestSource : Anibel.App.Services.IReleaseUpdateSource
{
    public bool IsInstalled => true;
    public string? PendingVersion => "99.0.0";
    public int ApplyCalls { get; private set; }
    public bool FailApply { get; set; }
    public Task<string?> CheckAsync() => Task.FromResult<string?>(PendingVersion);
    public Task DownloadAsync(Action<int> progress, CancellationToken ct) => Task.CompletedTask;
    public void PrepareRestart()
    {
        ApplyCalls++;
        if (FailApply) throw new InvalidOperationException("Test updater start failure");
    }
}

internal sealed partial class NativePlaybackSmoke(Application application, string directory)
{
    private Window? _window;
    private readonly List<string> _results = [];

    public void Start()
    {
        FFmpegInteropX.FFmpegInteropLogging.SetLogLevel(FFmpegInteropX.LogLevel.Warning);
        FFmpegInteropX.FFmpegInteropLogging.SetLogProvider(new DecoderTestLog());
        application.UnhandledException += (_, e) =>
        {
            File.WriteAllText(Path.Combine(directory, "result.txt"), "UNHANDLED " + e.Exception);
            e.Handled = true;
            Environment.Exit(1);
        };
        File.WriteAllText(Path.Combine(directory, "result.txt"), "Starting XAML smoke window");
        _window = new Window { Title = "Native playback test" };
        _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 500));
        var grid = new Grid();
        var video = new MediaPlayerElement { AreTransportControlsEnabled = false };
        var overlay = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(video);
        grid.Children.Add(overlay);
        _window.Content = grid;
        grid.Loaded += async (_, _) => await RunAsync(video, overlay);
        _window.Activate();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static byte[] Pixels(Image image)
    {
        using var stream = ((WriteableBitmap)image.Source).PixelBuffer.AsStream();
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message, int attempts = 150)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (condition()) return;
            await Task.Delay(100);
        }
        Check(condition(), message);
    }

    private async Task RunAsync(MediaPlayerElement video, Image overlay)
    {
        long handle = -1;
        try
        {
            var logo = new BitmapImage();
            var logoReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            logo.ImageOpened += (_, _) => logoReady.TrySetResult();
            logo.ImageFailed += (_, e) => logoReady.TrySetException(new InvalidOperationException(e.ErrorMessage));
            overlay.Source = logo;
            logo.UriSource = new Uri("ms-appx:///Assets/Logo.png");
            await logoReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(logo.PixelWidth > 0 && logo.PixelHeight > 0, "Published title-bar logo did not decode");
            overlay.Source = null;
            _results.Add("PASS published title-bar logo URI");
            var cachedImagePath = Path.Combine(directory, "cached-poster.image");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png"), cachedImagePath, true);
            var cachedImage = new BitmapImage();
            var cachedReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cachedImage.ImageOpened += (_, _) => cachedReady.TrySetResult();
            cachedImage.ImageFailed += (_, e) => cachedReady.TrySetException(new InvalidOperationException(e.ErrorMessage));
            overlay.Source = cachedImage;
            Check(Anibel.App.Services.ImageAddress.TryCreate(@"\\?\" + cachedImagePath, out var posterUri), "Extended poster path was rejected");
            cachedImage.UriSource = posterUri;
            await cachedReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(cachedImage.PixelWidth > 0, "Cached poster file did not decode");
            overlay.Source = null;
            _results.Add("PASS cached poster file URI");
            if (File.Exists(Path.Combine(directory, "fallback.ass")))
            {
                var fonts = Path.Combine(directory, "empty-fonts");
                Directory.CreateDirectory(fonts);
                var image = new Image();
                using var renderer = new AssRenderer(image, fonts);
                renderer.Load(Path.Combine(directory, "dialogue.ass"));
                renderer.Render(1, 640, 360, 640, 360, 1);
                var reference = Pixels(image);
                Check(reference.Any(b => b != 0), "System font rendering failed");
                renderer.Load(Path.Combine(directory, "fallback.ass"));
                renderer.Render(1, 640, 360, 640, 360, 1);
                Check(reference.SequenceEqual(Pixels(image)), "Missing font did not fall back to Arial");
                foreach (var dpi in new[] { 1.25, 1.5, 2.0, 1.0 })
                {
                    renderer.Render(1, 640, 360, 640, 360, dpi);
                    var bitmap = (WriteableBitmap)image.Source;
                    Check(bitmap.PixelWidth == (int)(640 * dpi) && bitmap.PixelHeight == (int)(360 * dpi), "DPI raster dimensions failed");
                    Check(image.Width == 640 && image.Height == 360 && Pixels(image).Any(b => b != 0), "DPI changed subtitle layout or lost glyphs");
                }
                _results.Add("PASS missing-font fallback and 100/125/150/200% DPI transitions");
            }
            var sources = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(directory, "sources.json")))!;
            var core = DispatchProxy.Create<ICoreClient, SelectionProxy>();
            handle = AnibelCoreNative.anibel_core_init(JsonSerializer.Serialize(new { dataDir = Path.Combine(directory, "core") }));
            Check(handle >= 0, "Test core init failed");
            ((SelectionProxy)(object)core).Handle = handle;
            foreach (var source in sources)
            {

                string? failure = null;
                var preferDub = source.EndsWith("#dub", StringComparison.Ordinal);

                var path = source.EndsWith("separate-audio", StringComparison.Ordinal) ? Path.Combine(directory, source.StartsWith("high10", StringComparison.Ordinal) ? "high10.mp4" : "input.mp4") : source;
                var audio = source.EndsWith("separate-audio", StringComparison.Ordinal) ? Path.Combine(directory, "audio.m4a") : null;
                var localMkv = Path.GetExtension(path) == ".mkv";
                var subs = localMkv ? Array.Empty<string>() : [Path.Combine(directory, "dialogue.ass")];
                var intent = new PlaybackIntentDto(PlaybackKind.Native, null, null, path, audio, null, [], [], null);
                var config = directory;
                if (source.Contains("video.anibel.net/", StringComparison.OrdinalIgnoreCase))
                {
                    var opened = await core.CallAsync<OpenPlaybackDto>("playbackOpen", new { url = source, episodeType = preferDub ? "dub" : "sub", preferDash = true });
                    intent = opened.Intent;
                    subs = opened.SubtitlePaths;
                    config = opened.ConfigDirectory;
                }
                File.AppendAllText(Path.Combine(directory, "progress.txt"), "Opening source\n");
                using var engine = new WindowsMediaEngine(core, video, overlay, config, preferDub);
                var seekAcknowledgements = 0;
                var audioSeekAcknowledgements = 0;
                video.MediaPlayer.PlaybackSession.SeekCompleted += (_, _) => Interlocked.Increment(ref seekAcknowledgements);
                var positionUpdates = 0;
                engine.PositionChanged += (_, _) => positionUpdates++;
                engine.Error += error => failure = error;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await engine.LoadAsync(intent, subs, timeout.Token);
                File.AppendAllText(Path.Combine(directory, "progress.txt"), "Source opened\n");
                var separateAudio = (Windows.Media.Playback.MediaPlayer?)typeof(WindowsMediaEngine)
                    .GetField("_audio", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);
                if (separateAudio is not null)
                    separateAudio.PlaybackSession.SeekCompleted += (_, _) => Interlocked.Increment(ref audioSeekAcknowledgements);
                await WaitUntilAsync(() => failure is not null || (engine.Position > 0.2 && engine.Duration > 3), "Playback clock/duration failed");
                try { await WaitUntilAsync(() =>
                {
                    if (failure is not null) return true;
                    try { return !engine.IsBuffering && video.MediaPlayer.PlaybackSession.NaturalVideoWidth > 0; }
                    catch (COMException) { return false; }
                }, "Decoded video did not become ready", 450); }
                catch
                {
                    _results.Add($"DECODER STATE software={engine.IsSoftwareDecoding} changing={engine.IsChangingSource} buffering={engine.IsBuffering} state={video.MediaPlayer.PlaybackSession.PlaybackState} clock={engine.Position} paused={engine.IsPaused}");
                    throw;
                }
                Check(failure is null, failure ?? "");
                if (source.Contains("high10", StringComparison.OrdinalIgnoreCase))
                    Check(engine.IsSoftwareDecoding, "10-bit H.264 did not use the software decoder");
                _results.Add(engine.IsSoftwareDecoding ? "DECODER software" : "DECODER Windows");
                if (File.Exists(Path.Combine(directory, "continuity-seconds.txt")))
                    await CheckContinuityAsync(engine, video.MediaPlayer, separateAudio);
                if (source.EndsWith("separate-audio", StringComparison.Ordinal))
                {
                    var audioPlayer = (Windows.Media.Playback.MediaPlayer)typeof(WindowsMediaEngine)
                        .GetField("_audio", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
                    Check(ReferenceEquals(video.MediaPlayer.TimelineController, audioPlayer.TimelineController), "Audio/video use different clocks");
                    void Buffer(string name, Windows.Media.Playback.MediaPlaybackSession session) => typeof(WindowsMediaEngine)
                        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [session, new object()]);
                    Buffer("OnBufferingStarted", video.MediaPlayer.PlaybackSession);
                    Buffer("OnBufferingStarted", audioPlayer.PlaybackSession);
                    await WaitUntilAsync(() => engine.IsBuffering, "Separate audio buffering was lost");
                    Buffer("OnBufferingEnded", video.MediaPlayer.PlaybackSession);
                    await Task.Delay(300);
                    Check(engine.IsBuffering, "Video resumed before audio was ready");
                    var held = engine.Position;
                    await Task.Delay(300);
                    Check(Math.Abs(engine.Position - held) < .05, "Shared clock ran during audio buffering");
                    Buffer("OnBufferingEnded", audioPlayer.PlaybackSession);
                    await WaitUntilAsync(() => !engine.IsBuffering && engine.Position > held + .1, "Shared clock did not resume");
                    Check(Math.Abs(video.MediaPlayer.PlaybackSession.Position.TotalSeconds - audioPlayer.PlaybackSession.Position.TotalSeconds) < .15, "Audio/video clocks diverged after buffering");
                    _results.Add("PASS separate audio/video buffering and synchronized resume");
                }
                Check(engine.ReadSubtitleTracks().Length > 0, "Subtitle track missing");
                Check(engine.ReadVideoTracks().Length > 0, "Video tracks missing");
                // Real dialogue may start after the opening. Seek to its first ASS event.
                var subtitlePosition = 2d;
                var selected = engine.ReadSubtitleTracks().FirstOrDefault(t => t.Id == engine.SubTrack);
                if (File.Exists(selected.FileName))
                {
                    var line = File.ReadLines(selected.FileName).FirstOrDefault(l => l.StartsWith("Dialogue:"));
                    if (line is not null && TimeSpan.TryParse(line.Split(',')[1], System.Globalization.CultureInfo.InvariantCulture, out var start))
                    {
                        engine.TogglePause();
                        await WaitUntilAsync(() => engine.IsPaused, "Pause before subtitle seek failed");
                        subtitlePosition = start.TotalSeconds + 0.2;
                        engine.Seek(subtitlePosition);
                        await WaitUntilAsync(() => !engine.IsSeeking && Math.Abs(video.MediaPlayer.TimelineController.Position.TotalSeconds - start.TotalSeconds - 0.2) < 0.1, "Subtitle seek failed");
                        engine.InvalidateSurface();
                        _results.Add($"SEEK target={start.TotalSeconds + 0.2} actual={engine.Position} duration={engine.Duration}");
                        engine.TogglePause();
                        await WaitUntilAsync(() => !engine.IsPaused, "Resume after subtitle seek failed");
                    }
                }
                engine.InvalidateSurface();
                Check(failure is null, failure ?? "");
                Check(overlay.Source is WriteableBitmap, "Subtitle overlay is empty");
                Check(Pixels(overlay).Any(b => b != 0), "libass produced no pixels");
                var audioTracks = engine.ReadAudioTracks();
                _results.Add($"TRACKS audio={JsonSerializer.Serialize(audioTracks)} subtitles={JsonSerializer.Serialize(engine.ReadSubtitleTracks().Select(t => new { t.Id, t.Title, t.Lang }))}");
                if (preferDub && audioTracks.Any(t => t.Lang is "be" or "bel"))
                    Check(audioTracks.Single(t => t.Id == engine.AudioTrack).Lang is "be" or "bel", "Dub preference failed");
                if (localMkv && Path.GetFileName(path) == "download.mkv")
                    Check(engine.ReadSubtitleTracks()[0].Title == "Субцітры", "UTF-8 MKV track title was damaged");
                engine.SetVolume(35);
                Check(Math.Abs(engine.Volume - 35) < 0.1, "Volume failed");
                engine.SetMute(true);
                Check(engine.IsMuted, "Mute failed");
                engine.SetMute(false);
                Check(!engine.IsMuted, "Unmute failed");
                Check(audioTracks.Length > 0, "Audio tracks missing");
                engine.SetAudioTrack(audioTracks[^1].Id);
                Check(engine.AudioTrack == audioTracks[^1].Id, "Audio selection failed");
                if (engine.IsAdaptive || engine.ReadVideoTracks().Length > 1)
                {
                    var tracks = engine.ReadVideoTracks();
                    Check(tracks.All(t => t.Height > 0), "Adaptive resolution metadata missing: " + JsonSerializer.Serialize(tracks));
                    var choices = await core.CallAsync<VideoQualitiesDto>("videoQualities", new { tracks });
                    Check(choices.Choices.All(c => c.Label == $"{tracks.Single(t => t.Id == c.Id).Height}p"), "Quality labels are not resolutions");
                    _results.Add("QUALITY " + string.Join(", ", choices.Choices.Select(c => c.Label)));
                    var highest = tracks.OrderByDescending(t => t.Height).ThenByDescending(t => t.Width).ThenByDescending(t => t.Bitrate).First();
                    Check(highest.Selected && !engine.IsAutomaticQuality, "Highest quality is not the default");
                    await WaitUntilAsync(() => video.MediaPlayer.PlaybackSession.NaturalVideoHeight == highest.Height, "Default output is not highest resolution");
                    if (!engine.IsPaused) engine.TogglePause();
                    var audioBefore = engine.AudioTrack;
                    var subBefore = engine.SubTrack;
                    foreach (var target in new[] { tracks.OrderBy(t => t.Height).First(), highest }.DistinctBy(t => t.Id))
                    {
                        var positionBefore = engine.Position;
                        var started = System.Diagnostics.Stopwatch.StartNew();
                        var change = engine.SetVideoTrackAsync(target.Id);
                        await WaitUntilAsync(() => change.IsCompleted || engine.IsChangingSource, "Quality switch did not start");
                        if (!change.IsCompleted)
                        {
                            positionBefore = Math.Min(engine.Duration - 0.5, positionBefore + 1);
                            engine.Seek(positionBefore);
                        }
                        await change;
                        await WaitUntilAsync(() => !engine.IsSeeking, "Quality/seek operation did not finish");
                        Check(engine.IsPaused && engine.AudioTrack == audioBefore && engine.SubTrack == subBefore, "Quality switch changed pause/audio/subtitles");
                        Check(Math.Abs(engine.Position - positionBefore) < 0.5, $"Quality switch lost playback position: expected {positionBefore}, actual {engine.Position}, duration {engine.Duration}");
                        // HLS can display a separate I-frame rendition while paused. Check normal decoded frames after resume.
                        engine.TogglePause();
                        try
                        {
                            await WaitUntilAsync(() => video.MediaPlayer.PlaybackSession.NaturalVideoHeight == target.Height, "Decoded output did not switch to " + target.Height);
                        }
                        catch
                        {
                            _results.Add($"SWITCH FAILED actual={video.MediaPlayer.PlaybackSession.NaturalVideoHeight} position={engine.Position} state={video.MediaPlayer.PlaybackSession.PlaybackState} error={failure}");
                            throw;
                        }
                        engine.TogglePause();
                        Check(engine.ReadVideoTracks().Single(t => t.Selected).Id == target.Id, "Quality selection was not retained");
                        _results.Add($"SWITCH {target.Height}p decoded in {started.Elapsed.TotalSeconds:F2}s");
                    }
                    engine.TogglePause();
                    Check(!engine.IsAutomaticQuality, "Manual quality failed");
                    if (engine.IsAdaptive)
                    {
                        engine.SetAutomaticQuality();
                        Check(engine.IsAutomaticQuality, "Automatic quality failed");
                    }
                }
                engine.TogglePause();
                await WaitUntilAsync(() => engine.IsPaused, "Pause state failed");
                await Task.Delay(300);
                var stopped = engine.Position;
                await Task.Delay(300);
                Check(Math.Abs(engine.Position - stopped) < 0.1, "Pause failed");
                var acknowledgementsBeforeSeek = Volatile.Read(ref seekAcknowledgements);
                var audioAcknowledgementsBeforeSeek = Volatile.Read(ref audioSeekAcknowledgements);
                engine.Seek(engine.Duration / 2);
                Check(Math.Abs(engine.Position - engine.Duration / 2) < 0.01, "Seek position did not update immediately");
                if (engine.IsSoftwareDecoding) Check(engine.IsSeeking && engine.IsBuffering, "Software seek was acknowledged before decoding completed");
                var seekDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                video.DispatcherQueue.TryEnqueue(() => seekDispatched.TrySetResult());
                await seekDispatched.Task;
                engine.Seek(2);
                engine.Seek(subtitlePosition);
                Check(Math.Abs(engine.Position - subtitlePosition) < 0.01, "Newest seek was lost");
                try { await WaitUntilAsync(() => !engine.IsSeeking, "Seek did not finish"); }
                catch
                {
                    _results.Add("SEEK RANGES " + string.Join(",", video.MediaPlayer.PlaybackSession.GetSeekableRanges().Select(r => $"{r.Start.TotalSeconds}+{r.End.TotalSeconds}")));
                    _results.Add($"SEEK FAILED updates={positionUpdates} requested={subtitlePosition} displayed={engine.Position} timeline={video.MediaPlayer.TimelineController.Position.TotalSeconds} session={video.MediaPlayer.PlaybackSession.Position.TotalSeconds} state={video.MediaPlayer.PlaybackSession.PlaybackState}");
                    throw;
                }
                await Task.Delay(500);
                Check(Math.Abs(engine.Position - subtitlePosition) < 0.3, "Seek finished at the wrong position");
                engine.SetSubTrack(0);
                Check(overlay.Source is null, "Subtitle off failed");
                engine.SetSubTrack(engine.ReadSubtitleTracks()[0].Id);
                engine.TogglePause();
                await WaitUntilAsync(() => !engine.IsPaused, "Resume state failed");
                try {
                await WaitUntilAsync(() => Math.Abs(video.MediaPlayer.PlaybackSession.Position.TotalSeconds - engine.Position) < 1 && engine.Position >= subtitlePosition && engine.Position < subtitlePosition + 20, "Decoded playback did not follow the new seek timeline");
                } catch {
                    _results.Add($"BUFFER DIAG buffer={engine.IsBuffering} pause={engine.IsPaused} seek={engine.IsSeeking} clock={engine.Position} decoded={video.MediaPlayer.PlaybackSession.Position} state={video.MediaPlayer.PlaybackSession.PlaybackState} timeline={video.MediaPlayer.TimelineController.State}");
                    throw;
                }
                Check(failure is null, failure ?? "");
                if (engine.IsSoftwareDecoding)
                {
                    Check(Volatile.Read(ref seekAcknowledgements) > acknowledgementsBeforeSeek,
                        "Software seek completed without a native acknowledgement");
                    Check(Math.Abs(video.MediaPlayer.PlaybackSession.Position.TotalSeconds - engine.Position) < 0.15,
                        "Software video clock disagrees with the seek position");
                    if (separateAudio is not null)
                    {
                        Check(Volatile.Read(ref audioSeekAcknowledgements) > audioAcknowledgementsBeforeSeek,
                            "Separate audio seek was not acknowledged");
                        Check(Math.Abs(separateAudio.PlaybackSession.Position.TotalSeconds - video.MediaPlayer.PlaybackSession.Position.TotalSeconds) < 0.15,
                            "Separate audio and video clocks disagree after seek");
                    }
                }
                var before = overlay.Width;
                _window!.AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 320));
                await WaitUntilAsync(() => overlay.Width < before || before < 480,
                    $"Subtitle resizing failed (previous width {before})");
                _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 500));
                if (source == sources[0])
                {
                    var mainGrid = (Grid)_window.Content;
                    mainGrid.Children.Clear();
                    var pipGrid = new Grid();
                    pipGrid.Children.Add(video);
                    pipGrid.Children.Add(overlay);
                    var pip = new Window { Content = pipGrid, Title = "PiP playback test" };
                    pip.AppWindow.Resize(new Windows.Graphics.SizeInt32(400, 260));
                    pip.Activate();
                    var position = engine.Position;
                    await WaitUntilAsync(() => engine.Position > position && overlay.Source is WriteableBitmap, "PiP transfer failed");
                    pipGrid.Children.Clear();
                    mainGrid.Children.Add(video);
                    mainGrid.Children.Add(overlay);
                    pip.Close();
                    await Task.Delay(200);
                    Check(overlay.Source is WriteableBitmap, "Return from PiP failed");
                }
                _results.Add("PASS " + source);
                File.WriteAllLines(Path.Combine(directory, "result.txt"), _results);
            }
            var episode = sources.FirstOrDefault(s => s.Contains("video.anibel.net/"));
            if (episode is not null)
            {
                using var services = new ServiceCollection().AddSingleton<ICoreClient>(core).BuildServiceProvider();
                typeof(Anibel.App.App).GetProperty(nameof(Anibel.App.App.Services))!.SetValue(null, services);
                var host = new PlayerHost();
                _window!.Content = host;
                try
                {
                    await host.PlayAsync(new Anibel.App.Views.PlayerArgs("", "anime", episode, "Playback test", "1", null, EpisodeType: "sub"));
                    await Task.Delay(1500);
                    Check(((Grid)host.FindName("ErrorHost")).Visibility == Visibility.Collapsed,
                        ((Anibel.App.Views.EmptyState)host.FindName("ErrorState")).Message);
                    var player = ((MediaPlayerElement)host.FindName("VideoPanel")).MediaPlayer;
                    await WaitUntilAsync(() =>
                    {
                        try { return !((ProgressRing)host.FindName("BusyRing")).IsActive && player.PlaybackSession.NaturalVideoWidth > 0; }
                        catch (COMException) { return false; }
                    }, "PlayerHost video is missing", 450);
                    // Inject native buffering notifications through the actual engine/host path.
                    // The timeline and visible loader must follow buffering, not play intent.
                    var controller = (PlayerController)typeof(PlayerHost).GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                    var native = (WindowsMediaEngine)controller.Engine!;
                    void BufferEvent(string name) => typeof(WindowsMediaEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(native, [player.PlaybackSession, new object()]);
                    BufferEvent("OnBufferingStarted");
                    await WaitUntilAsync(() => native.IsBuffering, "Buffering event was lost");
                    var ring = (ProgressRing)host.FindName("BusyRing");
                    Check(ring.IsActive && ((StackPanel)host.FindName("Overlay")).Visibility == Visibility.Visible, "Buffering loader is hidden");
                    var heldAt = player.TimelineController.Position;
                    await Task.Delay(400);
                    Check(Math.Abs((player.TimelineController.Position - heldAt).TotalSeconds) < .05, "Clock runs during buffering");
                    host.TogglePlayback();
                    Check(host.IsPaused, "Pause during buffering was lost");
                    BufferEvent("OnBufferingEnded");
                    await WaitUntilAsync(() => !native.IsBuffering, "Buffering did not end");
                    Check(!ring.IsActive, "Buffering loader did not stop");
                    heldAt = player.TimelineController.Position;
                    await Task.Delay(300);
                    Check(Math.Abs((player.TimelineController.Position - heldAt).TotalSeconds) < .05, "Buffering end overrode user pause");
                    host.TogglePlayback();
                    await WaitUntilAsync(() => player.TimelineController.Position > heldAt + TimeSpan.FromMilliseconds(100), "Playback did not resume after buffering");
                    _results.Add("PASS buffering loader, held clock, and pause intent");
                    await host.ShowQualityMenuAsync((Button)host.FindName("QualityButton"));
                    Check(host.IsQualityMenuOpen, "PlayerHost settings did not open");
                    host.SetMini(true);
                    host.SetMini(false);
                    host.TogglePlayback();
                    Check(host.IsPaused, "Pause button did not update immediately");
                    host.TogglePlayback();
                    Check(!host.IsPaused, "Quick resume click was lost");
                    host.TogglePlayback();
                    Check(host.IsPaused, "Quick pause click was lost");
                    await Task.Delay(600);
                    var pausedAt = player.TimelineController.Position.TotalSeconds;
                    await Task.Delay(300);
                    Check(Math.Abs(player.TimelineController.Position.TotalSeconds - pausedAt) < 0.1, "Rapid clicks did not leave playback paused");
                    ((Slider)host.FindName("SeekSlider")).Value = 8;
                    Check(((TextBlock)host.FindName("PositionText")).Text.StartsWith("0:08"), "Seek time did not update immediately");
                    Check(((ProgressRing)host.FindName("BusyRing")).IsActive, "Seek loading indicator is missing");
                    var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    host.DispatcherQueue.TryEnqueue(() => dispatched.TrySetResult());
                    await dispatched.Task;
                    Check(Math.Abs(((Slider)host.FindName("SeekSlider")).Value - 8) < 0.01, "Seek slider snapped back");
                    await WaitUntilAsync(() => Math.Abs(player.TimelineController.Position.TotalSeconds - 8) < 0.3, "PlayerHost slider seek failed");
                    await WaitUntilAsync(() => !((ProgressRing)host.FindName("BusyRing")).IsActive, "Seek loading indicator did not stop");
                    var button = (Button)host.FindName("QualityButton");
                    button.Focus(FocusState.Programmatic);
                    typeof(PlayerHost).GetMethod("ShowTheaterControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
                    Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(host.XamlRoot), button), "Showing controls stole button focus");
                    _results.Add("PASS PlayerHost load/settings/mini/rapid-pause-resume/seek/focus/close");
                }
                finally { await host.CloseAsync(); }
            }
            if (sources.Length == 0) await CheckShellAsync(core);
            _results.Add("ALL PASSED");
        }
        catch (Exception ex) { _results.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { if (handle >= 0) AnibelCoreNative.anibel_core_shutdown(handle); }
        File.WriteAllLines(Path.Combine(directory, "result.txt"), _results);
        _window!.Close();
        application.Exit();
    }
    private async Task CheckShellAsync(ICoreClient core)
    {
        var settings = new Anibel.App.Services.SettingsService();
        var updateSource = new RestartTestSource();
        using var services = new ServiceCollection()
            .AddSingleton(settings).AddSingleton(core)
            .AddSingleton<Anibel.App.Services.SearchHistory>()
            .AddSingleton<Anibel.App.Services.ICredentialStore, Anibel.App.Services.WindowsCredentialStore>()
            .AddSingleton<Anibel.App.Services.SessionService>()
            .AddTransient<Anibel.App.ViewModels.HomeViewModel>()
            .AddTransient<Anibel.App.ViewModels.CatalogViewModel>()
            .AddTransient<Anibel.App.ViewModels.SearchViewModel>()
            .AddTransient<Anibel.App.ViewModels.MediaDetailsViewModel>()
            .AddTransient<Anibel.App.ViewModels.ProfileViewModel>()
            .AddTransient<Anibel.App.ViewModels.ProfileListViewModel>()
            .AddSingleton<Anibel.App.Services.IReleaseUpdateSource>(updateSource)
            .AddSingleton<Anibel.App.Services.AppUpdateService>()
            .AddSingleton<Anibel.App.Services.DownloadService>()
            .AddTransient<Anibel.App.ViewModels.DownloadsViewModel>()
            .BuildServiceProvider();
        typeof(Anibel.App.App).GetProperty(nameof(Anibel.App.App.Services))!.SetValue(null, services);
        var shellStart = System.Diagnostics.Stopwatch.StartNew();
        var shell = new Anibel.App.MainWindow();
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((FrameworkElement)shell.Content).Loaded += (_, _) => loaded.TrySetResult();
        shell.Activate();
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        _results.Add($"STARTUP shell loaded {shellStart.Elapsed.TotalMilliseconds:F0} ms; process {DateTime.Now.Subtract(System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds:F0} ms");
        await Task.Delay(500);
        var root = (Grid)shell.Content;
        var notice = (InfoBar)root.FindName("UpdateNotice");
        notice.UpdateLayout();
        var noticeOrigin = notice.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        Check(notice.IsOpen && notice.ActualWidth <= 440 && notice.ActualWidth > 0,
            "Update notice is not a compact card");
        Check(Math.Abs(root.ActualWidth - noticeOrigin.X - notice.ActualWidth - 16) < 2
            && Math.Abs(root.ActualHeight - noticeOrigin.Y - notice.ActualHeight - 16) < 2,
            "Update notice is not anchored bottom-right");
        notice.IsOpen = false;
        _results.Add("PASS bottom-right update notification");
        var solid = (Border)root.FindName("SolidBackground");
        var search = (AutoSuggestBox)root.FindName("GlobalSearch");
        var account = (Button)root.FindName("AccountButton");
        var mainPage = (Anibel.App.MainPage)((Frame)root.FindName("RootFrame")).Content;
        Check(mainPage.KeyboardAcceleratorPlacementMode.ToString() == "Hidden", "Page exposes an Esc tooltip");
        var contentFrame = (Frame)mainPage.FindName("ContentFrame");
        contentFrame.Navigate(typeof(Page));
        var historyCount = contentFrame.BackStack.Count;
        Check(mainPage.HandleMouseNavigation(Microsoft.UI.Input.PointerUpdateKind.XButton1Released), "Mouse back was ignored");
        Check(contentFrame.BackStack.Count == historyCount - 1, "Mouse back did not navigate");
        Check(mainPage.HandleMouseNavigation(Microsoft.UI.Input.PointerUpdateKind.XButton2Released), "Mouse forward was ignored");
        Check(contentFrame.BackStack.Count == historyCount, "Mouse forward did not navigate");
        var playerChrome = new PlayerHost();
        Check(playerChrome.KeyboardAcceleratorPlacementMode.ToString() == "Hidden", "Player exposes a Space tooltip");
        var tip = new ToolTip { Content = "test", XamlRoot = account.XamlRoot, PlacementTarget = account };
        ToolTipService.SetToolTip(account, tip);
        tip.IsOpen = true;
        await Task.Delay(50);
        Anibel.App.Services.KeyboardNavigation.CloseToolTips(account.XamlRoot);
        Check(!tip.IsOpen, "Player tooltip cleanup did not close a tip");
        Check(settings.Background == Anibel.App.Services.WindowBackground.Mica, "Mica must be the default background");
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            Check(shell.SystemBackdrop is Microsoft.UI.Xaml.Media.MicaBackdrop, "Default Mica was not applied");
        foreach (var background in Enum.GetValues<Anibel.App.Services.WindowBackground>())
        {
            settings.Background = background;
            shell.ApplyBackground();
            await Task.Delay(100);
            if (background == Anibel.App.Services.WindowBackground.Mica && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                Check(shell.SystemBackdrop is Microsoft.UI.Xaml.Media.MicaBackdrop { Kind: Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt }, "Mica Alt was not applied");
            if (background == Anibel.App.Services.WindowBackground.Acrylic && Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
                Check(shell.SystemBackdrop is Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop, "Acrylic was not applied");
            Check(solid.Visibility == (shell.SystemBackdrop is null ? Visibility.Visible : Visibility.Collapsed), "Solid layer covers system backdrop");
        }
        account.Focus(FocusState.Programmatic);
        search.Focus(FocusState.Programmatic);
        Check(!Anibel.App.Services.KeyboardNavigation.FocusIsWithin(search), "Search accepted fallback focus from a loaded control");
        var searchOwner = (Anibel.App.ToolbarSearch)typeof(Anibel.App.MainWindow)
            .GetField("_toolbarSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        searchOwner.Focus();
        Check(Anibel.App.Services.KeyboardNavigation.FocusIsWithin(search), "Explicit search did not get focus");
        account.Focus(FocusState.Programmatic);
        account.Visibility = Visibility.Collapsed;
        await Task.Delay(100);
        Check(!Anibel.App.Services.KeyboardNavigation.FocusIsWithin(search), "Search took focus when a control disappeared");
        _results.Add("PASS shell solid/Mica/Acrylic and explicit-only search focus");
        foreach (var page in new Page[]
        {
            new Anibel.App.Views.CatalogPage(), new Anibel.App.Views.SearchPage(),
            new Anibel.App.Views.MediaDetailsPage(), new Anibel.App.Views.ProfilePage(),
            new Anibel.App.Views.ProfileListPage(), new Anibel.App.Views.DownloadsPage(), new Anibel.App.Views.SettingsPage(),
        })
        {
            _window!.Content = page;
            await Task.Delay(150);
            Check(page.IsLoaded, page.GetType().Name + " failed to load");
            if (page is Anibel.App.Views.MediaDetailsPage details)
            {
                details.Vm.Media = new MediaDetailDto { MediaType = "manga", Title = new TitleDto { Be = "Праверка" } };
                details.Vm.ShowChapters = true;
                details.Vm.Chapters.Add(new ChapterDto { Id = "test", Chapter = 1, Title = "Глава" });
                await Task.Delay(200);
                var chapters = (ListView)details.FindName("ChaptersList");
                chapters.UpdateLayout();
                Check(chapters.ContainerFromIndex(0) is ListViewItem, "Chapter template did not render");
            }
        }
        var downloads = services.GetRequiredService<Anibel.App.Services.DownloadService>();
        var row = new Anibel.App.Services.DownloadItem { Id = "ui-test", Title = "Download test", Status = Anibel.App.Services.DownloadStatus.Downloading };
        downloads.Items.Add(row);
        var downloadsPage = new Anibel.App.Views.DownloadsPage();
        _window!.Content = downloadsPage;
        await Task.Delay(200);
        row.Apply(new Anibel.App.Services.DownloadItem { Id = row.Id, Title = row.Title,
            Status = Anibel.App.Services.DownloadStatus.Downloading, ProgressKnown = true,
            Progress = .42, BytesReceived = 4200, BytesTotal = 10000, BytesPerSecond = 1000, RemainingSeconds = 6 });
        await Task.Delay(250);
        var activeContainer = downloadsPage.FindName("List") as ListView;
        var activeBar = Anibel.App.Services.KeyboardNavigation.Find<ProgressBar>(activeContainer!);
        Check(activeBar is { IsIndeterminate: false, Value: 42 }, "Download progress did not update");
        row.Apply(new Anibel.App.Services.DownloadItem { Id = row.Id, Title = row.Title,
            PosterPath = @"\\?\" + Path.Combine(directory, "cached-poster.image"),
            Status = Anibel.App.Services.DownloadStatus.Completed, CanPlay = true, CanSave = true, DiskBytes = 1024 });
        await Task.Delay(200);
        Check(!row.IsActive && row.IsCompleted && row.CanPlay, "Download row did not finish");
        Check(!downloadsPage.Vm.DiskLabel.EndsWith("0 Б"), "Download disk total is stale");
        var downloadList = Anibel.App.Services.KeyboardNavigation.Find<ListView>(downloadsPage)!;
        downloadList.UpdateLayout();
        var container = (ListViewItem)downloadList.ContainerFromIndex(0);
        Check(Anibel.App.Services.KeyboardNavigation.Find<ProgressBar>(container) is null, "Completed download still shows progress");
        _results.Add("PASS extended download poster path and completed download controls");
        var browser = new WebView2();
        _window!.Content = browser;
        var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(directory, "webview"), new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions());
        await browser.EnsureCoreWebView2Async(environment);
        browser.NavigateToString("<html><body>Playback browser test</body></html>");
        Check(await browser.CoreWebView2.ExecuteScriptAsync("1+1") == "2", "WebView2 script failed");
        browser.Close();
        _results.Add("PASS page construction, chapter template, and WebView2");
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        updateSource.FailApply = true;
        try { await shell.RestartForUpdateAsync(); throw new Exception("Updater failure was hidden"); }
        catch (InvalidOperationException ex) when (ex.Message == "Test updater start failure") { }
        Check(!closed && updateSource.ApplyCalls == 1, "Failed update silently closed the window");
        updateSource.FailApply = false;
        await shell.RestartForUpdateAsync();
        Check(closed && updateSource.ApplyCalls == 2, "Restart closed without invoking the updater");
        _results.Add("PASS update restart invokes updater before close and reports launch failure");
    }
}
