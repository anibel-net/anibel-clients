using System.Text.Json;
using Anibel.App.Core;
using Anibel.App.Playback;
using Xunit;
namespace Anibel.App.Tests;

public class PlayerControllerTests
{
    [Fact]
    public async Task Slow_reports_keep_latest_position_and_preserve_lifecycle_events()
    {
        var pending = new TaskCompletionSource<object?>();
        var core = new FakeCoreClient { Handler = (op, args, ct) => pending.Task };
        var engine = new FakeEngine { Duration = 2000 };
        using var controller = new PlayerController(core);
        controller.AttachForTesting(engine, 9);
        engine.RaiseReady();
        for (var i = 1; i <= 1000; i++) engine.RaisePositionChanged(i, 2000);
        engine.RaiseEnded();
        controller.Dispose();
        Assert.Single(core.Calls);
        pending.SetResult(new PlaybackActionDto(null));
        await controller.ReportsCompleted;
        Assert.Equal(new[] { "ready", "position", "ended", "close" }, core.Calls.Select(c => c.Args.GetProperty("event").GetString()));
        Assert.Equal(1000, core.Calls[1].Args.GetProperty("position").GetDouble());
    }

    [Fact]
    public async Task Reports_are_ordered_and_core_seek_is_applied()
    {
        var core = new FakeCoreClient { Handler = (op, args, ct) => Task.FromResult<object?>(new PlaybackActionDto(95)) };
        var engine = new FakeEngine { Position = 100, Duration = 600 };
        using var controller = new PlayerController(core);
        controller.AttachForTesting(engine, 7);
        engine.RaiseReady();
        await controller.ReportsCompleted;
        Assert.Equal(95, Assert.Single(engine.Seeks));
        engine.RaisePositionChanged(101, 600);
        engine.RaiseEnded();
        controller.Dispose();
        await controller.ReportsCompleted;
        Assert.Equal(new[] { "ready", "position", "ended", "close" }, core.Calls.Select(c => c.Args.GetProperty("event").GetString()));
        Assert.Equal(new ulong[] { 1, 2, 3, 4 }, core.Calls.Select(c => c.Args.GetProperty("sequence").GetUInt64()));
        Assert.All(core.Calls, c => Assert.Equal(7UL, c.Args.GetProperty("sessionId").GetUInt64()));
        Assert.True(engine.IsDisposed);
        Assert.Equal(0, engine.SubscriberCount);
    }
    [Fact]
    public async Task Late_core_action_cannot_seek_a_replacement_engine()
    {
        var pending = new TaskCompletionSource<object?>();
        var core = new FakeCoreClient { Handler = (op, args, ct) => pending.Task };
        var old = new FakeEngine { Position = 0, Duration = 600 };
        var next = new FakeEngine { Position = 0, Duration = 600 };
        using var controller = new PlayerController(core);
        controller.AttachForTesting(old, 1);
        old.RaiseReady();
        controller.AttachForTesting(next, 2);
        pending.SetResult(new PlaybackActionDto(95));
        await controller.ReportsCompleted;
        Assert.Empty(next.Seeks);
        Assert.Empty(old.Seeks);
    }
    [Fact]
    public void Native_controls_are_forwarded_and_events_are_unwired()
    {
        var engine = new FakeEngine();
        using var controller = new PlayerController(new FakeCoreClient());
        controller.Seek(10);
        controller.AttachForTesting(engine, 1);
        controller.TogglePause();
        controller.Seek(42);
        controller.SetVolume(55);
        controller.SetMute(true);
        controller.SetSubTrack(3);
        controller.Resize(1280, 720);
        controller.InvalidateSurface();
        Assert.Equal(1, engine.TogglePauseCalls);
        Assert.Equal(42, Assert.Single(engine.Seeks));
        Assert.Equal(55, Assert.Single(engine.VolumeSets));
        Assert.True(Assert.Single(engine.MuteSets));
        Assert.Equal(3, Assert.Single(engine.SubTrackSets));
        Assert.Equal((1280u, 720u), Assert.Single(engine.Resizes));
        Assert.Equal(1, engine.InvalidateSurfaceCalls);
        controller.Dispose();
        Assert.Equal(0, engine.SubscriberCount);
        engine.RaiseReady();
        engine.RaiseEnded();
        engine.RaiseError("late");
        engine.RaisePositionChanged(1, 2);
        engine.RaisePauseChanged(true);
    }
    [Fact]
    public async Task Closing_one_title_does_not_stop_or_seek_another_title()
    {
        var core = new FakeCoreClient();
        var firstEngine = new FakeEngine();
        var secondEngine = new FakeEngine();
        using var first = new PlayerController(core);
        using var second = new PlayerController(core);
        first.AttachForTesting(firstEngine, 10);
        second.AttachForTesting(secondEngine, 20);
        firstEngine.RaiseReady();
        secondEngine.RaiseReady();
        first.Dispose();
        await first.ReportsCompleted;
        Assert.True(firstEngine.IsDisposed);
        Assert.False(secondEngine.IsDisposed);
        second.Seek(90);
        secondEngine.RaisePositionChanged(90, 600);
        await second.ReportsCompleted;
        Assert.Equal(90, Assert.Single(secondEngine.Seeks));
        Assert.Empty(firstEngine.Seeks);
        Assert.DoesNotContain(core.Calls, c => c.Args.GetProperty("sessionId").GetUInt64() == 20
            && c.Args.GetProperty("event").GetString() == "close");
        Assert.Equal(new ulong[] { 1, 2 }, core.Calls
            .Where(c => c.Args.GetProperty("sessionId").GetUInt64() == 20)
            .Select(c => c.Args.GetProperty("sequence").GetUInt64()));
    }

    [Fact]
    public async Task Closing_during_source_load_waits_for_the_late_session_to_close()
    {
        var pending = new TaskCompletionSource<object?>();
        var core = new FakeCoreClient
        {
            Handler = (op, args, ct) => op == "playbackOpen"
                ? pending.Task : Task.FromResult<object?>(new PlaybackActionDto(null))
        };
        using var controller = new PlayerController(core);
        var opening = controller.StartAsync("test", "episode", new PlayerSurfaces
        {
            VideoPanel = null!, SubtitleOverlay = null!, EmbedHost = null!
        });
        controller.Dispose();
        var closing = controller.ReportsCompleted;
        Assert.False(closing.IsCompleted);
        pending.SetResult(new OpenPlaybackDto(33, null!, [], "", false));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        await closing;
        var report = Assert.Single(core.Calls.Where(c => c.Op == "playbackReport"));
        Assert.Equal(33UL, report.Args.GetProperty("sessionId").GetUInt64());
        Assert.Equal("close", report.Args.GetProperty("event").GetString());
    }

    [Theory]
    [InlineData(960, 1920, 1080)]
    [InlineData(720, 300, 200)]
    [InlineData(1280, 1920, 100)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(0, 1, 1)]
    public void Video_window_size_stays_inside_the_work_area(int requested, int width, int height)
    {
        var size = PipSize.Video(requested, width, height);
        Assert.InRange(size.Width, 1, width);
        Assert.InRange(size.Height, 1, height);
        Assert.InRange(Math.Abs(size.Height - size.Width * (9.0 / 16.0)), 0, 1);
    }

    private sealed class FakeEngine : IPlayerEngine, IPlaybackControls
    {
        public event Action? Ready;
        public event Action? Ended;
        public event Action<string>? Error;
        public event Action<double, double>? PositionChanged;
        public event Action<bool>? PauseChanged;

        public bool IsInitialized { get; set; } = true;
        public bool HasSubs
        {
            get; set;
        }
        public double Position { get; set; } = -1;
        public double Duration { get; set; } = -1;
        public double Volume { get; set; } = 100;
        public bool IsMuted
        {
            get; set;
        }
        public long SubTrack
        {
            get; set;
        }

        public bool IsDisposed
        {
            get; private set;
        }
        public List<string> LoadedVideoSources { get; } = [];
        public List<double> Seeks { get; } = [];
        public List<double> VolumeSets { get; } = [];
        public List<bool> MuteSets { get; } = [];
        public List<long> SubTrackSets { get; } = [];
        public List<(uint W, uint H)> Resizes { get; } = [];
        public int TogglePauseCalls
        {
            get; private set;
        }
        public int InvalidateSurfaceCalls
        {
            get; private set;
        }

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
