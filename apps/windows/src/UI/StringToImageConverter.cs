using Anibel.App.Services;
using Microsoft.UI.Xaml.Data;

namespace Anibel.App.Views;

public sealed class StringToImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => ImageLoader.Shared.Get(value, parameter);
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
