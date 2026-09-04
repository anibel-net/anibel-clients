using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.UI.Xaml.Data;

namespace Anibel.App.Views.Converters;

/// <summary>Computed display text for <see cref="SlideDto"/> — presentation only.</summary>
public static class SlideDisplay
{
    public static string Title(SlideDto slide) => Ui.Text(slide.Title?.Be, slide.Title?.Ru);
    public static string Content(SlideDto slide) => Ui.Text(slide.Content?.Be, slide.Content?.Ru) ?? "";
}

public sealed class SlideTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is SlideDto slide ? SlideDisplay.Title(slide) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class SlideContentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is SlideDto slide ? SlideDisplay.Content(slide) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
