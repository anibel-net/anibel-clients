using Anibel.App.Core;
using Microsoft.UI.Xaml.Controls;
namespace Anibel.App.Playback;

internal sealed class SubtitlePresentation(Image overlay) : IDisposable
{
    private readonly NativeSubtitleAssets _assets = new();
    private AssRenderer? _renderer;
    public IReadOnlyList<SubtitleTrackInfo> Tracks => _assets.Tracks;
    public async Task PrepareAsync(PlaybackIntentDto intent, IReadOnlyList<string> paths, string directory, CancellationToken ct)
    {
        await _assets.PrepareAsync(intent, paths, directory, ct);
        ct.ThrowIfCancellationRequested();
        _renderer = new AssRenderer(overlay, _assets.FontsDirectory);
    }
    public void Load(string? path) => _renderer?.Load(path);
    public void Render(double position, double width, double height, uint videoWidth, uint videoHeight)
        => _renderer?.Render(position, width, height, videoWidth, videoHeight, overlay.XamlRoot?.RasterizationScale ?? 1);
    public void StopRendering() { _renderer?.Dispose(); _renderer = null; }
    public void Dispose() { StopRendering(); _assets.Dispose(); }
}
