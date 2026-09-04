using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.UI.Xaml.Data;

namespace Anibel.App.Views.Converters;

/// <summary>Computed display text for <see cref="ChapterDto"/> — presentation only.</summary>
public static class ChapterDisplay
{
    public static string Label(ChapterDto ch) =>
        ch.EndChapter is double end && end > ch.Chapter
            ? $"{ch.Chapter:0.##}-{end:0.##}"
            : $"{ch.Chapter:0.##}";

    public static string TitleLabel(ChapterDto ch) =>
        string.IsNullOrWhiteSpace(ch.Title) ? Strings.ChapterTitle(Label(ch)) : ch.Title;
}

public sealed class ChapterNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is ChapterDto ch ? ChapterDisplay.Label(ch) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class ChapterTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is ChapterDto ch ? ChapterDisplay.TitleLabel(ch) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
