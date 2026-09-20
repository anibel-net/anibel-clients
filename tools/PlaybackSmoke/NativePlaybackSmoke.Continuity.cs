using System.Reflection;
using Anibel.App.Playback;

namespace PlaybackSmoke;

internal sealed partial class NativePlaybackSmoke
{
    private async Task CheckContinuityAsync(WindowsMediaEngine engine, Windows.Media.Playback.MediaPlayer player,
        Windows.Media.Playback.MediaPlayer? audio)
    {
        var seconds = int.Parse(File.ReadAllText(Path.Combine(directory, "continuity-seconds.txt")));
        Check(seconds is >= 10 and <= 120, "Continuity duration must be 10..120 seconds");
        _window!.AppWindow.Resize(new Windows.Graphics.SizeInt32(1920, 1080));
        if (File.Exists(Path.Combine(directory, "continuity-start.txt")))
        {
            engine.Seek(double.Parse(File.ReadAllText(Path.Combine(directory, "continuity-start.txt")), System.Globalization.CultureInfo.InvariantCulture));
            await WaitUntilAsync(() => !engine.IsSeeking && !engine.IsBuffering, "Continuity seek did not finish", 450);
        }
        var software = (SoftwareVideoSource?)typeof(WindowsMediaEngine)
            .GetField("_softwareSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);
        // This getter takes the decoder lock; never block the UI on it.
        var stream = software is null ? null : await Task.Run(() => software.Decoder.GetMediaStreamSource());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var samples = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Requested(Windows.Media.Core.MediaStreamSource sender, Windows.Media.Core.MediaStreamSourceSampleRequestedEventArgs args)
        {
            if (args.Request.StreamDescriptor is Windows.Media.Core.VideoStreamDescriptor && args.Request.Sample is { } sample)
                samples.Enqueue(FormattableString.Invariant($"{clock.Elapsed.TotalSeconds:F6},{sample.Timestamp.TotalSeconds:F6},{sample.Duration.TotalSeconds:F6}"));
        }
        if (stream is not null) stream.SampleRequested += Requested;
        var start = engine.Position;
        var previous = TimeSpan.Zero;
        var buffered = 0d;
        var maxUiGap = 0d;
        var frameTimes = new List<double>();
        void Frame(object? sender, object args) => frameTimes.Add(clock.Elapsed.TotalSeconds);
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Frame;
        var trace = new List<string> { "elapsed,position,video,audio,buffering,state,uiGap" };
        try
        {
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(100);
                var elapsed = clock.Elapsed;
                var gap = (elapsed - previous).TotalSeconds;
                previous = elapsed;
                maxUiGap = Math.Max(maxUiGap, gap);
                if (engine.IsBuffering) buffered += gap;
                trace.Add(FormattableString.Invariant($"{elapsed.TotalSeconds:F3},{engine.Position:F3},{player.PlaybackSession.Position.TotalSeconds:F3},{audio?.PlaybackSession.Position.TotalSeconds:F3},{engine.IsBuffering},{player.PlaybackSession.PlaybackState},{gap:F3}"));
            }
        }
        finally
        {
            if (stream is not null) stream.SampleRequested -= Requested;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Frame;
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 500));
        }
        var progress = engine.Position - start;
        var decoded = samples.ToArray();
        File.WriteAllLines(Path.Combine(directory, "decoded-samples.csv"), decoded);
        var frameGaps = frameTimes.Zip(frameTimes.Skip(1), (a, b) => b - a).Order().ToArray();
        var report = FormattableString.Invariant($"CONTINUITY wall={clock.Elapsed.TotalSeconds:F2}s progress={progress:F2}s buffered={buffered:F2}s maxUiGap={maxUiGap:F3}s");
        var renderReport = FormattableString.Invariant($"DECODE samples={decoded.Length} uiFrames={frameGaps.Length} p95UiFrame={frameGaps.ElementAtOrDefault((int)(frameGaps.Length * .95)):F3}s maxUiFrame={frameGaps.LastOrDefault():F3}s");
        _results.Add(report);
        _results.Add(renderReport);
        File.WriteAllLines(Path.Combine(directory, $"continuity-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv"), trace);
        File.AppendAllText(Path.Combine(directory, "progress.txt"), report + "\n" + renderReport + "\n");
        Check(stream is null || decoded.Length > 0, "No decoded software samples were measured");
        Check(progress >= seconds * .95, "Playback spent more than 5% of the continuity run stalled");
        Check(maxUiGap < .5, "Playback blocked the UI for half a second");
    }
}
