using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Anibel.App.Views.Converters;

/// <summary>
/// Computed display text for <see cref="MediaCard"/> — presentation only,
/// kept out of the transport DTO (see Dtos.cs).
/// </summary>
public static class CardDisplay
{
    public static string Title(MediaCard card) => Ui.Title(card.Title);

    public static string MetaLine(MediaCard card)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(card.MediaType)) parts.Add(Ui.MediaType(card.MediaType));
        if (card.Year is > 0) parts.Add(card.Year.Value.ToString());
        if (card.Rating is > 0) parts.Add($"{card.Rating.Value:0.0}");
        return string.Join(" · ", parts);
    }

    public static string StatusLabel(MediaCard card) => Ui.MediaStatus(card.Status);

    public static bool ShowStatus(MediaCard card) => StatusLabel(card).Length > 0;

    public static bool ShowSub(MediaCard card) => HasKind(card, "sub");
    public static bool ShowDub(MediaCard card) => HasKind(card, "dub");

    public static string LanguageLabel(MediaCard card)
    {
        if (TryLangUpdate(card, out var kind))
        {
            return Ui.Language(kind);
        }
        if (card.Language is not { Length: > 0 })
        {
            return "";
        }
        return string.Join(" · ", card.Language.Select(Ui.Language).Where(s => s.Length > 0).Distinct());
    }

    public static bool ShowLanguage(MediaCard card) => LanguageLabel(card).Length > 0;

    public static string NumLabel(MediaCard card)
    {
        if (card.Num is null or <= 0)
        {
            return "";
        }
        var n = card.Num.Value.ToString();
        return card.MediaType.Trim().ToLowerInvariant() switch
        {
            "manga" or "books" => Strings.NumChapters(n),
            "games" => Strings.NumGame(n),
            _ => Strings.NumEpisode(n),
        };
    }

    public static bool ShowNum(MediaCard card) => NumLabel(card).Length > 0;

    public static string InfoLine(MediaCard card)
    {
        var parts = new List<string>();
        if (ShowStatus(card)) parts.Add(StatusLabel(card));
        if (ShowLanguage(card)) parts.Add(LanguageLabel(card));
        if (ShowNum(card)) parts.Add(NumLabel(card));
        return string.Join(" · ", parts);
    }

    public static bool ShowInfo(MediaCard card) => InfoLine(card).Length > 0;

    private static bool TryLangUpdate(MediaCard card, out string kind)
    {
        kind = (card.UpdateType ?? "").Trim().ToLowerInvariant();
        return kind is "sub" or "dub";
    }

    private static bool HasKind(MediaCard card, string kind)
    {
        if (TryLangUpdate(card, out var update))
        {
            return update == kind;
        }
        return card.Language?.Any(x => string.Equals(x.Trim(), kind, StringComparison.OrdinalIgnoreCase)) == true;
    }
}

public sealed class CardTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is MediaCard card ? CardDisplay.Title(card) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class CardMetaLineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is MediaCard card ? CardDisplay.MetaLine(card) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class CardStatusLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is MediaCard card ? CardDisplay.StatusLabel(card) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class CardLanguageLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is MediaCard card ? CardDisplay.LanguageLabel(card) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class CardNumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is MediaCard card ? CardDisplay.NumLabel(card) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class CardInfoLineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is MediaCard card ? CardDisplay.InfoLine(card) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>MediaCard boolean display flags as Visibility. Set <see cref="Flag"/> in XAML.</summary>
public sealed class CardFlagVisibilityConverter : IValueConverter
{
    public string Flag { get; set; } = "status";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not MediaCard card)
        {
            return Visibility.Collapsed;
        }
        var show = Flag switch
        {
            "sub" => CardDisplay.ShowSub(card),
            "dub" => CardDisplay.ShowDub(card),
            "num" => CardDisplay.ShowNum(card),
            "info" => CardDisplay.ShowInfo(card),
            "language" => CardDisplay.ShowLanguage(card),
            _ => CardDisplay.ShowStatus(card),
        };
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
