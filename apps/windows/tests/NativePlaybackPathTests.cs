using Anibel.App.Playback;
using Xunit;

namespace Anibel.App.Tests;

public class NativePlaybackPathTests
{
    [Theory]
    [InlineData(@"\\?\C:\Downloads\эпізод.mkv", @"C:\Downloads\эпізод.mkv")]
    [InlineData(@"\\?\UNC\server\share\episode.mkv", @"\\server\share\episode.mkv")]
    [InlineData(@"C:\Downloads\episode.mkv", @"C:\Downloads\episode.mkv")]
    [InlineData(@"\\server\share\episode.mkv", @"\\server\share\episode.mkv")]
    public void Core_paths_are_accepted_by_Windows_storage(string path, string expected)
        => Assert.Equal(expected, WindowsMediaEngine.StoragePath(path));

    [Theory]
    [InlineData(640, 360, 1, 640, 360)]
    [InlineData(640, 360, 1.5, 960, 540)]
    [InlineData(640, 360, 2, 1280, 720)]
    [InlineData(1920, 1080, 3, 3840, 2160)]
    [InlineData(1080, 1920, 3, 1215, 2160)]
    [InlineData(640, 360, double.NaN, 640, 360)]
    public void Subtitle_pixels_follow_DPI_without_stretching_or_unbounded_allocation(
        double width, double height, double scale, int pixelsWide, int pixelsHigh)
        => Assert.Equal((pixelsWide, pixelsHigh), AssRenderer.RasterSize(width, height, scale));
}
