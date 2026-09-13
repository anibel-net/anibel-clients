using Anibel.App.Playback;
using Anibel.App.Reading;
using Windows.System;
using Xunit;

namespace Anibel.App.Tests;

public class ReaderLayoutTests
{
    [Fact]
    public void Page_moves_stay_inside_chapter_even_at_integer_limits()
    {
        Assert.Equal(0, ReaderPages.Move(0, -1, 20));
        Assert.Equal(19, ReaderPages.Move(19, 1, 20));
        Assert.Equal(0, ReaderPages.Move(5, 1, 0));
        Assert.Equal(19, ReaderPages.Move(int.MaxValue, int.MaxValue, 20));
        Assert.Equal(0, ReaderPages.Move(int.MinValue, int.MinValue, 20));
        Assert.Equal(0, ReaderPages.Clamp(8, 1));
    }

    [Fact]
    public void Reading_direction_changes_arrows_but_not_page_up_and_down()
    {
        Assert.Equal(6, ReaderPages.KeyPage(VirtualKey.Right, 5, 10, false));
        Assert.Equal(4, ReaderPages.KeyPage(VirtualKey.Right, 5, 10, true));
        Assert.Equal(6, ReaderPages.KeyPage(VirtualKey.Left, 5, 10, true));
        Assert.Equal(6, ReaderPages.KeyPage(VirtualKey.PageDown, 5, 10, true));
        Assert.Equal(4, ReaderPages.KeyPage(VirtualKey.PageUp, 5, 10, true));
        Assert.Equal(0, ReaderPages.KeyPage(VirtualKey.Home, 5, 10, true));
        Assert.Equal(9, ReaderPages.KeyPage(VirtualKey.End, 5, 10, true));
        Assert.Null(ReaderPages.KeyPage(VirtualKey.A, 5, 10, false));
    }

    [Fact]
    public void Reader_window_is_free_shape_with_room_for_controls()
    {
        Assert.Equal((900, 400), PipSize.Reader(900, 400, 1920, 1080, 1));
        Assert.Equal((480, 360), PipSize.Reader(1, 1, 1920, 1080, 1.5));
        Assert.Equal((200, 150), PipSize.Reader(int.MaxValue, int.MaxValue, 200, 150, 2));
        Assert.Equal((1, 1), PipSize.Reader(0, 0, 0, 0, double.NaN));
    }
}
