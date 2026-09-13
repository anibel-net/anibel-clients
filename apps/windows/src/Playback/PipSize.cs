namespace Anibel.App.Playback;

internal static class PipSize
{
    internal static (int Width, int Height) Reader(int width, int height, int maxWidth, int maxHeight, double scale)
    {
        maxWidth = Math.Max(1, maxWidth);
        maxHeight = Math.Max(1, maxHeight);
        scale = double.IsFinite(scale) ? Math.Clamp(scale, 1, 4) : 1;
        return (Math.Clamp(width, Math.Min((int)(320 * scale), maxWidth), maxWidth),
            Math.Clamp(height, Math.Min((int)(240 * scale), maxHeight), maxHeight));
    }

    internal static (int Width, int Height) Video(int requestedWidth, int maxWidth, int maxHeight)
    {
        maxWidth = Math.Max(1, maxWidth);
        maxHeight = Math.Max(1, maxHeight);
        var limit = Math.Max(1, Math.Min(maxWidth, (int)Math.Min(int.MaxValue, maxHeight * (16.0 / 9.0))));
        var width = Math.Clamp(requestedWidth, Math.Min(400, limit), limit);
        return (width, Math.Clamp((int)Math.Round(width * (9.0 / 16.0)), 1, maxHeight));
    }
}
