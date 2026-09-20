using Windows.System;

namespace Anibel.App.Views;

internal static class GridKeyboard
{
    internal static int? Target(int index, int count, int columns, VirtualKey key)
    {
        if (index < 0 || count <= 0 || index >= count) return null;
        long? target = key switch
        {
            VirtualKey.Right or VirtualKey.J => (long)index + 1,
            VirtualKey.Left or VirtualKey.K => (long)index - 1,
            VirtualKey.Down => (long)index + Math.Max(1, columns),
            VirtualKey.Up => (long)index - Math.Max(1, columns),
            VirtualKey.Home => 0,
            VirtualKey.End => count - 1,
            _ => null,
        };
        return target is { } value ? (int)Math.Clamp(value, 0, count - 1L) : null;
    }
}
