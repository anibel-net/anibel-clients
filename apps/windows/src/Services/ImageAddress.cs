namespace Anibel.App.Services;

internal static class ImageAddress
{
    internal static bool TryCreate(string value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Rust returns extended DOS/UNC paths. WinRT URI does not accept the '?' host.
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) value = @"\\" + value[8..];
        else if (value.StartsWith(@"\\?\", StringComparison.Ordinal)) value = value[4..];
        return Uri.TryCreate(value, UriKind.Absolute, out uri!)
            && uri.Scheme is "file" or "http" or "https" or "ms-appx" or "ms-appdata";
    }
}
