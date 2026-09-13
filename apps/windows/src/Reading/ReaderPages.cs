using Windows.System;

namespace Anibel.App.Reading;

// Page position is native view state, like the scroll offset. Chapter policy stays in Rust.
internal static class ReaderPages
{
    internal static int Clamp(int page, int count) => count <= 0 ? 0 : Math.Clamp(page, 0, count - 1);
    internal static int Move(int page, int delta, int count) => count <= 0 ? 0
        : (int)Math.Clamp((long)page + delta, 0, (long)count - 1);
    internal static int? KeyPage(VirtualKey key, int page, int count, bool rightToLeft) => key switch
    {
        VirtualKey.Left => Move(page, rightToLeft ? 1 : -1, count),
        VirtualKey.Right => Move(page, rightToLeft ? -1 : 1, count),
        VirtualKey.PageUp => Move(page, -1, count),
        VirtualKey.PageDown => Move(page, 1, count),
        VirtualKey.Home => 0,
        VirtualKey.End => Clamp(int.MaxValue, count),
        _ => null,
    };
}
