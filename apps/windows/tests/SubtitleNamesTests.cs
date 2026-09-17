using Anibel.App.Core;
using Anibel.App.Playback;
using Xunit;

namespace Anibel.App.Tests;

public sealed class SubtitleNamesTests
{
    [Fact]
    public void UsesSourceNameEvenWhenCacheNameLostUnicode()
    {
        SubtitleTrackDto[] tracks = [new("", [], "%D0%9D%D0%B0%D0%B4%D0%BF%D1%96%D1%81%D1%8B")];
        Assert.Equal("Надпісы", SubtitleNames.ForAsset("sub-0-D09DD0B0.ass", tracks));
    }

    [Theory]
    [InlineData("sub-3-Dialogue.ass", "Dialogue")]
    [InlineData("sub-1-Надпісы.ass", "Надпісы")]
    [InlineData("Dialogue.ass", "Dialogue")]
    public void LocalAssetsKeepReadableNames(string path, string expected)
        => Assert.Equal(expected, SubtitleNames.ForAsset(path, []));
}
