using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Anibel.App.Views;

/// <summary>
/// Image URL → BitmapImage. Instances are cached so ItemsRepeater recycle
/// does not flash posters when scrolling back to already-seen cards.
/// </summary>
public sealed class StringToImageConverter : IValueConverter
{
    private const int MaxCached = 384;
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> Order = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url || !TryUri(url, out var uri))
        {
            return null!;
        }

        var height = parameter switch
        {
            int n when n > 0 => n,
            string raw when int.TryParse(raw, out var parsed) && parsed > 0 => parsed,
            _ => 0,
        };
        var width = parameter is string spec && spec.StartsWith("width:", StringComparison.Ordinal)
            && int.TryParse(spec.AsSpan(6), out var requestedWidth) ? Math.Clamp(requestedWidth, 1, 4096) : 0;
        var key = width > 0 ? url + "\0w" + width : height > 0 ? url + "\0" + height : url;
        if (Cache.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var image = new BitmapImage();
        if (width > 0)
        {
            image.DecodePixelType = DecodePixelType.Logical;
            image.DecodePixelWidth = width;
        }
        else if (height > 0)
        {
            image.DecodePixelType = DecodePixelType.Logical;
            image.DecodePixelHeight = height;
        }
        image.UriSource = uri;
        if (Cache.TryAdd(key, image))
        {
            Order.Enqueue(key);
            Trim();
            return image;
        }
        return Cache.TryGetValue(key, out var raced) ? raced : image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    private static void Trim()
    {
        while (Cache.Count > MaxCached && Order.TryDequeue(out var old))
        {
            Cache.TryRemove(old, out _);
        }
    }

    private static bool TryUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!))
        {
            return true;
        }
        if (System.IO.File.Exists(value))
        {
            uri = new Uri(value);
            return true;
        }
        uri = null!;
        return false;
    }
}
