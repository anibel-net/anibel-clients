namespace Anibel.App.Views;

/// <summary>
/// Column math for the poster grid: fill the viewport width by growing cells,
/// keep a 2:3 poster box, and leave the title block at a fixed text size.
/// </summary>
public static class PosterGridLayout
{
    public const double MinItemWidth = 168;
    public const double PosterAspect = 1.5; // height / width
    public const double TitleHeight = 68;
    public const double CardMargin = 6;
    public const double ColumnGap = 8;

    public static double CardGutter => CardMargin * 2;

    public readonly record struct Measure(int Columns, double ItemWidth, double ItemHeight, double SlotWidth);

    public static Measure ForViewport(double viewportWidth)
    {
        var width = Math.Max(0, viewportWidth);
        var minW = MinItemWidth - CardGutter;
        if (width < 1)
        {
            return ForItemWidth(Math.Max(1, minW), 1);
        }

        var columns = Math.Max(1, (int)Math.Floor((width + ColumnGap) / (minW + ColumnGap)));
        var itemWidth = (width - (columns - 1) * ColumnGap) / columns;
        return ForItemWidth(Math.Max(1, itemWidth), columns);
    }

    private static Measure ForItemWidth(double itemWidth, int columns)
    {
        var itemHeight = itemWidth * PosterAspect + TitleHeight;
        return new Measure(columns, itemWidth, itemHeight, itemWidth);
    }
}
