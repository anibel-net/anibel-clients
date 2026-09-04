using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.UI.Xaml.Data;

namespace Anibel.App.Views.Converters;

/// <summary>Computed display text for <see cref="CommentDto"/> — presentation only.</summary>
public static class CommentDisplay
{
    public static string Username(CommentDto comment) => comment.User?.Username ?? Strings.Anonymous;
    public static string? Avatar(CommentDto comment) => comment.User?.Avatar;
    public static string DateLabel(CommentDto comment) => comment.Created > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(comment.Created).ToLocalTime().ToString("g")
        : "";
}

public sealed class CommentUsernameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is CommentDto comment ? CommentDisplay.Username(comment) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class CommentDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is CommentDto comment ? CommentDisplay.DateLabel(comment) : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
