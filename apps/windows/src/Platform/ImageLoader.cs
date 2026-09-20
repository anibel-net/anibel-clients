using Microsoft.UI.Xaml.Media.Imaging;

namespace Anibel.App.Services;

/// <summary>
/// Image URL → BitmapImage. Instances are cached so ItemsRepeater recycle
/// does not flash posters when scrolling back to already-seen cards.
/// </summary>
public sealed class ImageLoader
{
    private const int MaxCached = 384;
    private readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> Order = [];
    private bool _stopped;
    public static ImageLoader Shared { get; } = new();

    public object Get(object value, object parameter)
    {
        if (_stopped || value is not string url || !TryUri(url, out var uri))
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
        if (Cache.TryAdd(key, image))
        {
            Order.AddLast(key);
            Trim();
            image.ImageFailed += (_, _) =>
            {
                Remove(key, image);
                if (uri.Scheme is "http" or "https") ImageDiskCache.Shared.Invalidate(url);
            };
            _ = LoadAsync(image, uri, url, key);
            return image;
        }
        return Cache.TryGetValue(key, out var raced) ? raced : image;
    }

    private async Task LoadAsync(BitmapImage image, Uri uri, string url, string key)
    {
        try
        {
            var source = uri.Scheme is "http" or "https"
                ? new Uri(await ImageDiskCache.Shared.GetAsync(url)) : uri;
            if (!_stopped) image.UriSource = source;
        }
        catch (Exception ex)
        {
            Remove(key, image);
            if (!_stopped) Diag.Log($"image load: {ex.GetType().Name}");
            // Keep images usable when the cache directory cannot be written.
            if (!_stopped && ex is System.IO.IOException or UnauthorizedAccessException) image.UriSource = uri;
        }
    }

    private void Remove(string key, BitmapImage? expected = null)
    {
        if (expected is not null && (!Cache.TryGetValue(key, out var current) || !ReferenceEquals(current, expected))) return;
        Cache.Remove(key);
        Order.Remove(key);
    }

    private void Trim()
    {
        while (Cache.Count > MaxCached && Order.First is { } first) Remove(first.Value);
    }

    public async Task StopAsync()
    {
        _stopped = true;
        Cache.Clear();
        Order.Clear();
        await ImageDiskCache.Shared.StopAsync();
    }

    internal static bool TryUri(string value, out Uri uri)
        => ImageAddress.TryCreate(value, out uri);
}
