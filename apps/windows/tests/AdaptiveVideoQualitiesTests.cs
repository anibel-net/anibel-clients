using Anibel.App.Playback;
using Xunit;

namespace Anibel.App.Tests;

public class AdaptiveVideoQualitiesTests
{
    [Theory]
    [InlineData("avc1.6E0028", true)]
    [InlineData("avc3.6e002a", true)]
    [InlineData("avc1.7a0028", true)]
    [InlineData("avc1.f40028", true)]
    [InlineData("avc1.640028", false)]
    [InlineData("avc1.4d401f", false)]
    public void Unsupported_profiles_bypass_the_Windows_decoder(string codec, bool software)
    {
        Assert.Equal(software, AdaptiveVideoQualities.RequiresSoftwareDecoder($"<Representation codecs=\"{codec}\" />"));
        Assert.Equal(software, AdaptiveVideoQualities.RequiresSoftwareDecoder($"#EXT-X-STREAM-INF:CODECS=\"{codec},mp4a.40.2\""));
    }

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
    [Fact]
    public void Software_hls_keeps_audio_and_makes_relative_addresses_absolute()
    {
        var selected = AdaptiveVideoQualities.Select("""
            #EXTM3U
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="dub",URI="audio/dub.m3u8"
            #EXT-X-STREAM-INF:BANDWIDTH=9000,RESOLUTION=1920x1080,AUDIO="dub"
            high.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=3000,RESOLUTION=640x360,AUDIO="dub"
            low.m3u8
            #EXT-X-I-FRAME-STREAM-INF:BANDWIDTH=500,URI="iframe.m3u8"
            """, new Uri("https://example.test/video/master.m3u8"), 3000);
        Assert.Single(AdaptiveVideoQualities.Parse(selected));
        Assert.Contains("https://example.test/video/audio/dub.m3u8", selected);
        Assert.Contains("https://example.test/video/low.m3u8", selected);
        Assert.DoesNotContain("high.m3u8", selected);
        Assert.DoesNotContain("iframe", selected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<BaseURL>./</BaseURL>")]
    public void Software_dash_keeps_audio_and_segment_templates(string baseUrl)
    {
        var selected = AdaptiveVideoQualities.Select($"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011">{baseUrl}<Period>
            <AdaptationSet mimeType="video/mp4"><SegmentTemplate media="$RepresentationID$/$Number$.m4s" />
              <Representation id="high" bandwidth="9000" width="1920" height="1080" />
              <Representation id="low" bandwidth="3000" width="640" height="360" />
            </AdaptationSet>
            <AdaptationSet mimeType="audio/mp4"><Representation id="dub" bandwidth="128000" /></AdaptationSet>
            </Period></MPD>
            """, new Uri("https://example.test/video/manifest.mpd"), 3000);
        Assert.Single(AdaptiveVideoQualities.Parse(selected));
        Assert.Contains("https://example.test/video/", selected);
        Assert.Contains("$RepresentationID$/$Number$.m4s", selected);
        Assert.Contains("id=\"dub\"", selected);
        Assert.DoesNotContain("id=\"high\"", selected);
    }

    [Fact]
    public void Software_selection_rejects_unknown_bitrate()
        => Assert.Throws<ArgumentOutOfRangeException>(() => AdaptiveVideoQualities.Select("#EXTM3U", new Uri("https://example.test/master.m3u8"), 1));
}
