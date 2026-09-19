using Anibel.App.Services;
using Xunit;

namespace Anibel.App.Tests;

public class ImageAddressTests
{
    [Theory]
    [InlineData(@"\\?\C:\Users\Tester\Відэа\poster.jpg", @"C:\Users\Tester\Відэа\poster.jpg")]
    [InlineData(@"\\?\UNC\server\share\poster.jpg", @"\\server\share\poster.jpg")]
    public void Extended_download_paths_become_valid_file_uris(string source, string expected)
    {
        Assert.True(ImageAddress.TryCreate(source, out var uri));
        Assert.True(uri.IsFile);
        Assert.Equal(new Uri(expected), uri);
        Assert.NotEqual("?", uri.Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://[invalid")]
    [InlineData("javascript:alert(1)")]
    public void Invalid_images_do_not_throw_during_download_refresh(string source)
        => Assert.False(ImageAddress.TryCreate(source, out _));
}
