using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Anibel.App.Views;

/// <summary>Horizontal flow that wraps to the next line instead of scrolling sideways.</summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(6d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(6d, OnLayoutPropertyChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WrapPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        var limit = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        var x = 0d;
        var y = 0d;
        var rowHeight = 0d;
        var width = 0d;
        var spacingX = HorizontalSpacing;
        var spacingY = VerticalSpacing;

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(availableSize);
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > limit)
            {
                y += rowHeight + spacingY;
                x = 0;
                rowHeight = 0;
            }

            x += size.Width + spacingX;
            rowHeight = Math.Max(rowHeight, size.Height);
            width = Math.Max(width, x - spacingX);
        }

        return new Size(width, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        var y = 0d;
        var rowHeight = 0d;
        var spacingX = HorizontalSpacing;
        var spacingY = VerticalSpacing;

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                y += rowHeight + spacingY;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + spacingX;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        return finalSize;
    }
}
