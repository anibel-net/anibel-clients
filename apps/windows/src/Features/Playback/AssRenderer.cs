using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Anibel.App.Playback;

/// <summary>Renders libass masks into a premultiplied BGRA XAML overlay on the UI thread.</summary>
internal sealed class AssRenderer : IDisposable
{
    private const string Library = "libass-9.dll";
    private IntPtr _library, _renderer, _track;
    private readonly Image _image;
    private WriteableBitmap? _bitmap;
    private byte[] _pixels = [], _mask = [];
    private bool _dirty = true;

    public AssRenderer(Image image, string fonts)
    {
        _image = image;
        _library = ass_library_init();
        if (_library == IntPtr.Zero) throw new InvalidOperationException("libass initialization failed.");
        try
        {
            ass_set_fonts_dir(_library, fonts);
            _renderer = ass_renderer_init(_library);
            if (_renderer == IntPtr.Zero) throw new InvalidOperationException("Subtitle renderer initialization failed.");
            // DirectWrite discovers Windows fonts; explicit fonts are loaded from the episode directory.
            ass_set_fonts(_renderer, IntPtr.Zero, "Arial", 4, IntPtr.Zero, 1);
        }
        catch { Dispose(); throw; }
    }

    public void Load(string? path)
    {
        if (_track != IntPtr.Zero) ass_free_track(_track);
        _track = IntPtr.Zero;
        _image.Source = null;
        _dirty = true;
        if (path is null) return;
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Subtitle file is too large.");
        var data = File.ReadAllBytes(path);
        _track = ass_read_memory(_library, data, (UIntPtr)data.Length, "UTF-8");
        if (_track == IntPtr.Zero) throw new InvalidOperationException("The ASS subtitle file could not be opened.");
    }

    public void Render(double seconds, double width, double height, uint videoWidth, uint videoHeight, double rasterizationScale)
    {
        if (_track == IntPtr.Zero || width < 1 || height < 1 || !double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(seconds)) return;
        // Render only the fitted video rectangle, so positions also stay correct in PiP.
        var ratio = videoWidth > 0 && videoHeight > 0 ? (double)videoWidth / videoHeight : width / height;
        var fittedWidth = Math.Min(width, height * ratio);
        var fittedHeight = fittedWidth / ratio;
        var (w, h) = RasterSize(fittedWidth, fittedHeight, rasterizationScale);
        _image.Width = fittedWidth;
        _image.Height = fittedHeight;
        if (_bitmap is null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
        {
            _bitmap = new WriteableBitmap(w, h);
            _pixels = new byte[checked(w * h * 4)];
            ass_set_frame_size(_renderer, w, h);
            ass_set_storage_size(_renderer, (int)videoWidth, (int)videoHeight);
            _dirty = true;
        }
        var head = ass_render_frame(_renderer, _track, (long)(Math.Max(0, seconds) * 1000), out var changed);
        if (!_dirty && changed == 0) return;
        _dirty = false;
        Array.Clear(_pixels);
        for (var node = head; node != IntPtr.Zero;)
        {
            var item = Marshal.PtrToStructure<AssImage>(node);
            node = item.Next;
            if (item.Width <= 0 || item.Height <= 0 || item.Stride < item.Width || item.Bitmap == IntPtr.Zero) continue;
            var left = Math.Max(0, item.X);
            var top = Math.Max(0, item.Y);
            var right = (int)Math.Min(w, (long)item.X + item.Width);
            var bottom = (int)Math.Min(h, (long)item.Y + item.Height);
            if (right <= left || bottom <= top) continue;
            var count = right - left;
            if (_mask.Length < count) _mask = new byte[count];
            var opacity = 255 - (int)(item.Color & 255);
            var red = (int)(item.Color >> 24);
            var green = (int)((item.Color >> 16) & 255);
            var blue = (int)((item.Color >> 8) & 255);
            for (var y = top; y < bottom; y++)
            {
                var offset = checked((y - item.Y) * item.Stride + left - item.X);
                Marshal.Copy(IntPtr.Add(item.Bitmap, offset), _mask, 0, count);
                for (var x = 0; x < count; x++)
                {
                    var alpha = (_mask[x] * opacity + 127) / 255;
                    var inverse = 255 - alpha;
                    var pixel = (y * w + left + x) * 4;
                    _pixels[pixel] = (byte)((blue * alpha + _pixels[pixel] * inverse + 127) / 255);
                    _pixels[pixel + 1] = (byte)((green * alpha + _pixels[pixel + 1] * inverse + 127) / 255);
                    _pixels[pixel + 2] = (byte)((red * alpha + _pixels[pixel + 2] * inverse + 127) / 255);
                    _pixels[pixel + 3] = (byte)(alpha + (_pixels[pixel + 3] * inverse + 127) / 255);
                }
            }
        }
        using var stream = _bitmap.PixelBuffer.AsStream();
        stream.Write(_pixels);
        _bitmap.Invalidate();
        _image.Source = _bitmap;
    }

    internal static (int Width, int Height) RasterSize(double width, double height, double dpiScale)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(dpiScale) || dpiScale <= 0) dpiScale = 1;
        // Keep XAML dimensions in DIPs, but draw glyphs at the display's pixel density.
        // Apply one limit to both axes, preserving aspect ratio with at most 32 MiB of pixels.
        var scale = Math.Min(dpiScale, Math.Min(3840 / width, 2160 / height));
        return (Math.Clamp((int)Math.Ceiling(width * scale), 1, 3840),
                Math.Clamp((int)Math.Ceiling(height * scale), 1, 2160));
    }

    public void Dispose()
    {
        _image.Source = null;
        if (_track != IntPtr.Zero) ass_free_track(_track);
        if (_renderer != IntPtr.Zero) ass_renderer_done(_renderer);
        if (_library != IntPtr.Zero) ass_library_done(_library);
        _track = _renderer = _library = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AssImage
    {
        public int Width, Height, Stride;
        public IntPtr Bitmap;
        public uint Color;
        public int X, Y;
        public IntPtr Next;
        public int Type;
    }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ass_library_init();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ass_library_done(IntPtr library);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ass_renderer_init(IntPtr library);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ass_renderer_done(IntPtr renderer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ass_set_fonts_dir(IntPtr library, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ass_set_fonts(IntPtr renderer, IntPtr font, [MarshalAs(UnmanagedType.LPUTF8Str)] string family, int provider, IntPtr config, int update);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ass_read_memory(IntPtr library, byte[] data, UIntPtr size, [MarshalAs(UnmanagedType.LPUTF8Str)] string codepage);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ass_free_track(IntPtr track);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ass_set_frame_size(IntPtr renderer, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void ass_set_storage_size(IntPtr renderer, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ass_render_frame(IntPtr renderer, IntPtr track, long milliseconds, out int changed);
}
