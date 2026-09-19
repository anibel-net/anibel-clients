using Anibel.App.Playback;
using Xunit;

namespace Anibel.App.Tests;

public class AdaptiveVideoQualitiesTests
{
    [Fact]
    public void Hls_maps_peak_bitrates_to_resolution_and_ignores_other_playlists()
    {
        var sizes = AdaptiveVideoQualities.Parse("""
            #EXTM3U
            #EXT-X-MEDIA:TYPE=AUDIO,BANDWIDTH=128000,URI="audio.m3u8"
            #EXT-X-STREAM-INF:CODECS="avc1.640028,mp4a.40.2",AVERAGE-BANDWIDTH=2000000,BANDWIDTH=16000000,RESOLUTION=1920x1080
            high.m3u8
            #EXT-X-STREAM-INF:RESOLUTION=854x480,BANDWIDTH=1000000
            low.m3u8
            #EXT-X-I-FRAME-STREAM-INF:BANDWIDTH=500000,RESOLUTION=1920x1080,URI="iframe.m3u8"
            """);
        Assert.Equal(2, sizes.Count);
        Assert.Equal((1920u, 1080u), sizes[16000000]);
        Assert.Equal((854u, 480u), sizes[1000000]);
    }

    [Fact]
    public void Dash_reads_inherited_dimensions_and_excludes_audio()
    {
        var sizes = AdaptiveVideoQualities.Parse("""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011"><Period>
              <AdaptationSet mimeType="video/mp4" width="1920" height="1080">
                <Representation bandwidth="6000000" />
                <Representation bandwidth="3000000" width="1280" height="720" />
              </AdaptationSet>
              <AdaptationSet mimeType="audio/mp4"><Representation bandwidth="128000" /></AdaptationSet>
            </Period></MPD>
            """);
        Assert.Equal(2, sizes.Count);
        Assert.Equal((1920u, 1080u), sizes[6000000]);
        Assert.Equal((1280u, 720u), sizes[3000000]);
    }

    [Theory]
    [InlineData("<MPD>")]
    [InlineData("<!DOCTYPE MPD SYSTEM 'file:///not-read'><MPD />")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=4294967296,RESOLUTION=1920x1080")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=1920x999999999999")]
    public void Invalid_metadata_does_not_invent_a_resolution(string manifest)
        => Assert.Empty(AdaptiveVideoQualities.Parse(manifest));
}
