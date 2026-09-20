using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Anibel.App.Views.Converters;

/// <summary>Computed display text for <see cref="EpisodeDto"/> — presentation only.</summary>
public static class EpisodeDisplay
{
    public static string Number(EpisodeDto ep) =>
        ep.EndEpisode is double end && end > (ep.Episode ?? 0)
            ? $"{ep.Episode:0.##}-{end:0.##}"
            : $"{ep.Episode:0.##}";

    public static string TitleLabel(EpisodeDto ep) =>
        string.IsNullOrWhiteSpace(ep.Title) ? Strings.EpisodeTitle(Number(ep)) : ep.Title;

    public static string ResourceLabel(EpisodeDto ep) => ep.Resource switch
    {
        1 => "Drive",
        2 => "Anibel",
        _ => ep.Resource is { } r ? $"R{r}" : "",
    };

    public static string SourceCaption(EpisodeDto ep) => ResourceLabel(ep);

    public static Visibility ResourceBadgeVisible(EpisodeDto ep) =>
        string.IsNullOrEmpty(ResourceLabel(ep)) ? Visibility.Collapsed : Visibility.Visible;

    public static double WatchedOpacity(bool? watched) => watched == true ? 1.0 : 0.25;

    public static string TypeLabel(EpisodeDto ep) => (ep.Type ?? "EP").ToUpperInvariant();
}

public sealed class EpisodeNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is EpisodeDto ep ? EpisodeDisplay.Number(ep) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class EpisodeTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is EpisodeDto ep ? EpisodeDisplay.TitleLabel(ep) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class EpisodeSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is EpisodeDto ep ? EpisodeDisplay.SourceCaption(ep) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class EpisodeResourceBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is EpisodeDto ep ? EpisodeDisplay.ResourceBadgeVisible(ep) : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Maps the transport <c>Watched</c> bool? to the row opacity (1.0 / 0.25).</summary>
public sealed class EpisodeWatchedOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Note: the pattern must be "bool" (not "bool?") — nullable types are illegal in
        // patterns (CS8116), and "is bool? x ? ... : ..." also fails to parse (CS1003).
        if (value is bool watched)
        {
            return EpisodeDisplay.WatchedOpacity(watched);
        }

        return 0.25;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
