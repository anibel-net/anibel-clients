using Anibel.App.Services;
using Anibel.App.Views;
using Windows.System;
using Xunit;

namespace Anibel.App.Tests;

public class KeyboardTests
{
    [Fact]
    public void Every_navigation_binding_uses_a_valid_prefix()
    {
        foreach (var binding in ShortcutMap.Bindings.Where(b => b.AfterG))
        {
            var map = new ShortcutMap();
            Assert.Null(map.Read(VirtualKey.G, 0, false, TimeSpan.Zero));
            Assert.Equal(binding, map.Read(binding.Key, 0, false, TimeSpan.FromSeconds(1)));
        }
    }

    [Fact]
    public void Prefix_expires_and_does_not_survive_text_input()
    {
        var map = new ShortcutMap();
        map.Read(VirtualKey.G, 0, false, TimeSpan.Zero);
        Assert.Null(map.Read(VirtualKey.H, 0, false, TimeSpan.FromSeconds(2)));
        map.Read(VirtualKey.G, 0, false, TimeSpan.FromSeconds(3));
        Assert.Null(map.Read(VirtualKey.H, 0, true, TimeSpan.FromSeconds(3.1)));
        Assert.Null(map.Read(VirtualKey.H, 0, false, TimeSpan.FromSeconds(3.2)));
    }

    [Fact]
    public void Typing_and_modified_keys_do_not_navigate()
    {
        foreach (var binding in ShortcutMap.Bindings)
            Assert.Null(new ShortcutMap().Read(binding.Key, binding.Modifiers, true, TimeSpan.Zero));
        Assert.Null(new ShortcutMap().Read(VirtualKey.J, VirtualKeyModifiers.Control, false, TimeSpan.Zero));
        Assert.Equal(ShortcutAction.Search, new ShortcutMap().Read(VirtualKey.K,
            VirtualKeyModifiers.Control, false, TimeSpan.Zero)?.Action);
    }

    [Theory]
    [InlineData(0, 20, 4, VirtualKey.Left, 0)]
    [InlineData(0, 20, 4, VirtualKey.Up, 0)]
    [InlineData(3, 20, 4, VirtualKey.Right, 4)]
    [InlineData(3, 20, 4, VirtualKey.Down, 7)]
    [InlineData(7, 20, 4, VirtualKey.Up, 3)]
    [InlineData(17, 19, 4, VirtualKey.Down, 18)]
    [InlineData(18, 19, 4, VirtualKey.J, 18)]
    [InlineData(18, 19, 4, VirtualKey.K, 17)]
    [InlineData(9, 19, 4, VirtualKey.Home, 0)]
    [InlineData(9, 19, 4, VirtualKey.End, 18)]
    [InlineData(int.MaxValue - 2, int.MaxValue, 4, VirtualKey.Down, int.MaxValue - 1)]
    public void Grid_navigation_stays_within_loaded_items(int index, int count, int columns, VirtualKey key, int expected)
        => Assert.Equal(expected, GridKeyboard.Target(index, count, columns, key));

    [Fact]
    public void Empty_grid_and_native_activation_keys_are_left_to_the_control()
    {
        Assert.Null(GridKeyboard.Target(-1, 0, 4, VirtualKey.Right));
        Assert.Null(GridKeyboard.Target(0, 10, 4, VirtualKey.Enter));
        Assert.Null(GridKeyboard.Target(0, 10, 4, VirtualKey.Space));
    }
}
