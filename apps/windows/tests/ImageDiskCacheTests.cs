using System.Net;
using Anibel.App.Services;
using Anibel.App.Playback;
using Xunit;

namespace Anibel.App.Tests;

public sealed class ImageDiskCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Anibel-cache-test-" + Guid.NewGuid());

    [Fact]
    public async Task Cache_survives_restart_and_uses_complete_URL()
    {
        using var handler = new ImageHandler();
        using var client = new HttpClient(handler);
        var cache = new ImageDiskCache(_directory, client);
        var paths = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cache.GetAsync("https://example.org/poster?v=1")));
        Assert.Equal(1, handler.Requests);
        Assert.All(paths, path => Assert.Equal(paths[0], path));
        var restarted = new ImageDiskCache(_directory, client);
        Assert.Equal(paths[0], await restarted.GetAsync("https://example.org/poster?v=1"));
        Assert.Equal(1, handler.Requests);
        Assert.NotEqual(paths[0], await restarted.GetAsync("https://example.org/poster?v=2"));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task Failed_download_can_retry_and_does_not_leave_a_file()
    {
        using var handler = new ImageHandler { Fail = true };
        using var client = new HttpClient(handler);
        var cache = new ImageDiskCache(_directory, client);
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync("https://example.org/poster"));
        Assert.Empty(Directory.GetFiles(_directory));
        handler.Fail = false;
        Assert.True(File.Exists(await cache.GetAsync("https://example.org/poster")));
    }

    [Theory]
    [InlineData(965.73, "16:05")]
    [InlineData(3661.9, "1:01:01")]
    [InlineData(0, "0:00")]
    [InlineData(-1, "0:00")]
    [InlineData(double.NaN, "0:00")]
    [InlineData(double.PositiveInfinity, "0:00")]
    public void Seek_tooltip_shows_time(double seconds, string expected)
        => Assert.Equal(expected, PlaybackTimeConverter.Format(seconds));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class ImageHandler : HttpMessageHandler
    {
        public int Requests;
        public bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            await Task.Delay(10, cancellationToken);
            return new HttpResponseMessage(Fail ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            { Content = new ByteArrayContent([1, 2, 3]) };
        }
    }
}
