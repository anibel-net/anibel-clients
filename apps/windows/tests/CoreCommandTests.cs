using Anibel.App.Core;
using System.Text.Json;
using Xunit;
namespace Anibel.App.Tests;
public sealed class CoreCommandTests
{
    [Fact]
    public void WireNamesMatchSharedContract()
    {
        using var stream = typeof(CoreCommandTests).Assembly.GetManifestResourceStream("commands.json")!;
        var expected = JsonSerializer.Deserialize<string[]>(stream)!;
        Assert.Equal(expected.Order(), Enum.GetValues<CoreCommand>().Select(c => c.WireName()).Order());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((CoreCommand)int.MaxValue).WireName());
    }
}
