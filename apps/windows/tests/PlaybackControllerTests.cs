using Anibel.App.Core;
using Anibel.App.Playback;
using Anibel.App.Services;
using Xunit;

namespace Anibel.App.Tests;

/// <summary>
/// PlayerController tests driven through local fakes — no mpv, no WebView2, no
/// XAML/UI thread. Resume storage is injected via <see cref="IResumeStore"/> so
/// the real LocalAppData-backed store is never touched (SessionService is a
/// required ctor dependency and only reads/may-create its own base directory).
///
/// Skipped by design: StartLocalAsync "drives the fake engine with a local
/// video path" is NOT covered here because (1) PlayerSurfaces requires real
/// WinUI SwapChainPanel/Panel instances, which only exist on a WinUI UI thread
/// (the test host has none, and this project's test csproj is UseWinUI=false),
/// and (2) the native path hard-codes MpvEngine + Initialize/PreferDubAudio,
/// which are engine-specific and bind to libmpv. The wiring seam
/// (PlayerController.AttachForTesting) covers the identical Wire/Unwire path
/// deterministically instead.
/// </summary>
public class PlayerControllerTests
{
    [Fact]
    public void PositionChanged_saves_throttled_and_teardown_forces_save()
    {
        var store = new FakeResumeStore();
        var engine = new FakeEngine { Position = 10, Duration = 600 };
        using var controller = NewController(engine, store, "ep1");

        // First position change: outside the 5s window and >=1s delta → save.
        engine.RaisePositionChanged(10, 600);
        Assert.Single(store.Sets);
        Assert.Equal(("ep1", 10.0), store.Sets[0]);

        // Immediate second change: still inside the 5s throttle → no save.
        engine.Position = 12;
        engine.RaisePositionChanged(12, 600);
        Assert.Single(store.Sets);

        // Teardown (Dispose → TearDownEngine) force-saves the latest position.
        controller.Dispose();
        Assert.Equal(2, store.Sets.Count);
        Assert.Equal(("ep1", 12.0), store.Sets[1]);
        Assert.True(engine.IsDisposed);
    }

    [Fact]
    public void Resume_below_five_seconds_is_never_saved()
    {
        var store = new FakeResumeStore();
        var engine = new FakeEngine { Position = 3 };
        using var controller = NewController(engine, store, "ep1");

        engine.RaisePositionChanged(3, 600);
        Assert.Empty(store.Sets);

        controller.Dispose(); // forced save on teardown skips too (3 < 5)
        Assert.Empty(store.Sets);
    }

    [Fact]
    public void Ready_restores_resume_only_within_bounds()
    {
        var store = new FakeResumeStore();
        store.Values["ep1"] = 100;
        var engine = new FakeEngine { Position = 0, Duration = 600 };
        using var controller = NewController(engine, store, "ep1");

        engine.RaiseReady();
        Assert.Equal(new[] { 95.0 }, engine.Seeks);

        // Out-of-bounds resume (beyond Duration - 15) must not seek.
        var store2 = new FakeResumeStore();
        store2.Values["ep2"] = 2000;
        var engine2 = new FakeEngine { Position = 0, Duration = 600 };
        using var controller2 = NewController(engine2, store2, "ep2");
        engine2.RaiseReady();
        Assert.Empty(engine2.Seeks);
    }

    [Fact]
    public void Ended_clears_resume()
    {
        var store = new FakeResumeStore();
        store.Values["ep1"] = 42;
        var engine = new FakeEngine();
        using var controller = NewController(engine, store, "ep1");

        engine.RaiseEnded();

        Assert.Equal(new[] { "ep1" }, store.Clears);
        Assert.False(store.Values.ContainsKey("ep1"));
    }

    [Fact]
    public void Dispose_unwires_engine_events()
    {
        var store = new FakeResumeStore();
        var engine = new FakeEngine();
        var ready = 0;
        var ended = 0;
        var errors = 0;
        var positions = 0;
        var pauses = 0;
        var controller = NewController(engine, store, "ep1");
        controller.Ready += () => ready++;
        controller.Ended += () => ended++;
        controller.Error += _ => errors++;
        controller.PositionChanged += (_, _) => positions++;
        controller.PauseChanged += _ => pauses++;

        controller.Dispose();

        // Unwire detached every handler before the engine was disposed.
        Assert.True(engine.IsDisposed);
        Assert.Equal(0, engine.SubscriberCount);

        // Raising events on the disposed fake engine must neither call back
        // nor throw (this was the unsubscribe gap: TearDownEngine used to
        // Dispose without Unwire).
        engine.RaiseReady();
        engine.RaiseEnded();
        engine.RaiseError("boom");
        engine.RaisePositionChanged(1, 2);
        engine.RaisePauseChanged(true);

        Assert.Equal(0, ready + ended + errors + positions + pauses);
    }

    [Fact]
    public void Transport_calls_are_noops_without_controls()
    {
        using var controller = new PlayerController(new FakeCoreClient(), new SessionService());
        Assert.Null(controller.Controls);

        controller.TogglePause();
        controller.Seek(10);
        controller.SetVolume(50);
        controller.SetMute(true);
        controller.SetSubTrack(1);
        controller.Resize(1920, 1080);
        controller.InvalidateSurface();
        // Reaching this point without NullReference is the assertion.
    }

    [Fact]
    public void Transport_calls_route_to_controls()
    {
        var engine = new FakeEngine();
        using var controller = NewController(engine, new FakeResumeStore(), "ep1");

        controller.TogglePause();
        controller.Seek(42);
        controller.SetVolume(55);
        controller.SetMute(true);
        controller.SetSubTrack(3);
        controller.Resize(1280, 720);
        controller.InvalidateSurface();

        Assert.Equal(1, engine.TogglePauseCalls);
        Assert.Equal(new[] { 42.0 }, engine.Seeks);
        Assert.Equal(new[] { 55.0 }, engine.VolumeSets);
        Assert.Equal(new[] { true }, engine.MuteSets);
        Assert.Equal(new[] { 3L }, engine.SubTrackSets);
        Assert.Equal(new[] { (1280u, 720u) }, engine.Resizes);
        Assert.Equal(1, engine.InvalidateSurfaceCalls);
    }

    private static PlayerController NewController(FakeEngine engine, FakeResumeStore store, string episodeId)
    {
        var controller = new PlayerController(new FakeCoreClient(), new SessionService(), resumeStore: store);
        controller.AttachForTesting(engine, episodeId);
        return controller;
    }

    /// <summary>In-memory IResumeStore; records every call.</summary>
    private sealed class FakeResumeStore : IResumeStore
    {
        public Dictionary<string, double> Values { get; } = new();
        public List<(string Id, double Position)> Sets { get; } = [];
        public List<string> Clears { get; } = [];

        public double Get(string episodeId) => Values.TryGetValue(episodeId, out var pos) ? pos : 0;

        public void Set(string episodeId, double positionSecs)
        {
            Values[episodeId] = positionSecs;
            Sets.Add((episodeId, positionSecs));
        }

        public void Clear(string episodeId)
        {
            Values.Remove(episodeId);
            Clears.Add(episodeId);
        }
    }

    /// <summary>Local IPlayerEngine + IPlaybackControls fake (no native code).</summary>
    private sealed class FakeEngine : IPlayerEngine, IPlaybackControls
    {
        public event Action? Ready;
        public event Action? Ended;
        public event Action<string>? Error;
        public event Action<double, double>? PositionChanged;
        public event Action<bool>? PauseChanged;

        public bool IsInitialized { get; set; } = true;
        public bool HasSubs { get; set; }
        public double Position { get; set; } = -1;
        public double Duration { get; set; } = -1;
        public double Volume { get; set; } = 100;
        public bool IsMuted { get; set; }
        public long SubTrack { get; set; }

        public bool IsDisposed { get; private set; }
        public List<string> LoadedVideoSources { get; } = [];
        public List<double> Seeks { get; } = [];
        public List<double> VolumeSets { get; } = [];
        public List<bool> MuteSets { get; } = [];
        public List<long> SubTrackSets { get; } = [];
        public List<(uint W, uint H)> Resizes { get; } = [];
        public int TogglePauseCalls { get; private set; }
        public int InvalidateSurfaceCalls { get; private set; }

        public int SubscriberCount =>
            (Ready?.GetInvocationList().Length ?? 0) +
            (Ended?.GetInvocationList().Length ?? 0) +
            (Error?.GetInvocationList().Length ?? 0) +
            (PositionChanged?.GetInvocationList().Length ?? 0) +
            (PauseChanged?.GetInvocationList().Length ?? 0);

        public void Load(PlaybackIntentDto intent, IReadOnlyList<string> subPaths)
            => LoadAsync(intent, subPaths).GetAwaiter().GetResult();

        public Task LoadAsync(PlaybackIntentDto intent, IReadOnlyList<string> subPaths, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LoadedVideoSources.Add(intent.VideoSrc ?? intent.PageUrl ?? "");
            return Task.CompletedTask;
        }

        public void TogglePause() => TogglePauseCalls++;
        public void Seek(double seconds) => Seeks.Add(seconds);
        public void SetVolume(double volume) => VolumeSets.Add(volume);
        public void SetMute(bool mute) => MuteSets.Add(mute);
        public void SetSubTrack(long id) => SubTrackSets.Add(id);
        public void Resize(uint width, uint height) => Resizes.Add((width, height));
        public void InvalidateSurface() => InvalidateSurfaceCalls++;

        public void RaiseReady() => Ready?.Invoke();
        public void RaiseEnded() => Ended?.Invoke();
        public void RaiseError(string message) => Error?.Invoke(message);
        public void RaisePositionChanged(double pos, double dur) => PositionChanged?.Invoke(pos, dur);
        public void RaisePauseChanged(bool paused) => PauseChanged?.Invoke(paused);

        public void Dispose() => IsDisposed = true;
    }
}
