using Microsoft.UI.Xaml.Data;

namespace Anibel.App.Playback;

public sealed class PlaybackTimeConverter : IValueConverter
{
    internal static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return "0:00";
        var total = (long)Math.Min(Math.Floor(seconds), long.MaxValue / 2);
        return total >= 3600
            ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }

    public object Convert(object value, Type targetType, object parameter, string language)
        => Format(value is double seconds ? seconds : 0);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
