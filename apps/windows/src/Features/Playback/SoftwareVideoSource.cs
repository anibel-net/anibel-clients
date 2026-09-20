using FFmpegInteropX;
namespace Anibel.App.Playback;

/// <summary>Owns a software decoder and its local single-quality manifest.</summary>
internal sealed class SoftwareVideoSource(FFmpegMediaSource decoder, string? manifestPath) : IDisposable
{
    public FFmpegMediaSource Decoder { get; } = decoder;
    public static async Task<SoftwareVideoSource> OpenAsync(string path,
        (byte[] Data, Uri Uri, string ContentType)? manifestData,
        IReadOnlyDictionary<uint, (uint Width, uint Height)> videoSizes, uint? bitrate, CancellationToken ct)
    {
        Anibel.App.Services.Diag.Log("Software decoder: opening source");
        var config = new MediaSourceConfig();
        config.Video.VideoDecoderMode = VideoDecoderMode.ForceFFmpegSoftwareDecoder;
        config.General.FastSeekSmartStreamSwitching = false;
        config.Subtitles.UseEmbeddedSubtitleFonts = false; // libass owns extracted fonts.
        config.FFmpegOptions["rw_timeout"] = "15000000";
        config.FFmpegOptions["protocol_whitelist"] = "file,http,https,tcp,tls,crypto,data";
        string? manifestPath = null;
        FFmpegMediaSource decoder;
        // Await the native factory to completion so cancellation cannot lose its native handle.
        // Network reads are bounded by rw_timeout; close waits for this operation.
        ct.ThrowIfCancellationRequested();
        try
        {
            if (manifestData is { } manifest && videoSizes.Count > 0)
            {
                var selected = bitrate ?? videoSizes.OrderByDescending(t => t.Value.Height)
                    .ThenByDescending(t => t.Value.Width).ThenByDescending(t => t.Key).First().Key;
                var text = AdaptiveVideoQualities.Select(System.Text.Encoding.UTF8.GetString(manifest.Data), manifest.Uri, selected);
                var extension = manifest.ContentType == "application/dash+xml" ? ".mpd" : ".m3u8";
                manifestPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Anibel-playback-" + Guid.NewGuid().ToString("N") + extension);
                await System.IO.File.WriteAllTextAsync(manifestPath, text, ct);
                decoder = await FFmpegMediaSource.CreateFromFileAsync(manifestPath, config);
            }
            else decoder = await (System.IO.File.Exists(path)
                ? FFmpegMediaSource.CreateFromFileAsync(WindowsMediaEngine.StoragePath(path), config)
                : FFmpegMediaSource.CreateFromUriAsync(path, config));
        }
        catch { if (manifestPath is not null) System.IO.File.Delete(manifestPath); throw; }
        if (ct.IsCancellationRequested)
        {
            decoder.Dispose();
            if (manifestPath is not null) System.IO.File.Delete(manifestPath);
            ct.ThrowIfCancellationRequested();
            throw new ObjectDisposedException(nameof(SoftwareVideoSource));
        }
        Anibel.App.Services.Diag.Log("Software decoder: source ready");
        return new SoftwareVideoSource(decoder, manifestPath);
    }
    public void Dispose()
    {
        Decoder.Dispose();
        if (manifestPath is not null) System.IO.File.Delete(manifestPath);
    }
}
