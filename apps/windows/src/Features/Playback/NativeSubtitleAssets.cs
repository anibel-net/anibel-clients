using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Anibel.App.Core;

namespace Anibel.App.Playback;

/// <summary>Temporary ASS files and fonts for native playback, including portable MKV files.</summary>
internal sealed class NativeSubtitleAssets : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Anibel-subtitles-" + Guid.NewGuid().ToString("N"));
    public string FontsDirectory => Path.Combine(_directory, "fonts");
    public List<SubtitleTrackInfo> Tracks { get; } = [];

    public async Task PrepareAsync(PlaybackIntentDto intent, IReadOnlyList<string> paths, string configDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(FontsDirectory);
        var fonts = Path.Combine(configDirectory, "fonts");
        if (Directory.Exists(fonts))
            foreach (var file in Directory.EnumerateFiles(fonts).Take(128))
                if (new FileInfo(file).Length <= 32 * 1024 * 1024)
                    File.Copy(file, Path.Combine(FontsDirectory, Path.GetFileName(file)), true);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var title = SubtitleNames.ForAsset(path, intent.Subtitles);
            var ass = path;
            if (!Path.GetExtension(path).Equals(".ass", StringComparison.OrdinalIgnoreCase)
                && !Path.GetExtension(path).Equals(".ssa", StringComparison.OrdinalIgnoreCase))
            {
                ass = Path.Combine(_directory, $"external-{Tracks.Count}.ass");
                await RunAsync("ffmpeg", ["-v", "error", "-nostdin", "-y", "-i", path, "-map", "0:s:0", "-c:s", "ass", ass], ct);
            }
            Tracks.Add(new(Tracks.Count + 1, title, "", ass));
        }
        // Windows decodes the MKV; extract only text tracks/fonts for libass.
        if (intent.VideoSrc is not { } source || !File.Exists(source)
            || !Path.GetExtension(source).Equals(".mkv", StringComparison.OrdinalIgnoreCase)) return;
        var probe = await RunAsync("ffprobe", ["-v", "error", "-show_entries", "stream=index,codec_type,codec_name:stream_tags=title,language,mimetype", "-of", "json", source], ct);
        using var document = JsonDocument.Parse(probe);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Length > 128) throw new InvalidOperationException("Too many MKV tracks.");
        foreach (var stream in streams)
        {
            var index = stream.GetProperty("index").GetInt32();
            var type = stream.GetProperty("codec_type").GetString();
            string Tag(string key) => stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
            if (type == "subtitle")
            {
                var codec = stream.GetProperty("codec_name").GetString();
                // Image subtitles remain available through the Windows timed metadata track.
                if (codec is not ("ass" or "ssa" or "subrip" or "webvtt" or "mov_text" or "text")) continue;
                var path = Path.Combine(_directory, $"embedded-{index}.ass");
                await RunAsync("ffmpeg", ["-v", "error", "-nostdin", "-y", "-i", source, "-map", $"0:{index}", "-c:s", codec == "ass" ? "copy" : "ass", path], ct);
                var title = Tag("title");
                Tracks.Add(new(Tracks.Count + 1, string.IsNullOrWhiteSpace(title) ? $"Субцітры {Tracks.Count + 1}" : title, Tag("language"), path));
            }
            else if (type == "attachment" && Tag("mimetype") is "application/x-truetype-font" or "application/vnd.ms-opentype" or "font/ttf" or "font/otf")
            {
                var ext = Tag("mimetype") is "font/otf" or "application/vnd.ms-opentype" ? "otf" : "ttf";
                var path = Path.Combine(FontsDirectory, $"font-{index}.{ext}");
                // A bounded packet copy gives FFmpeg an output while it extracts the attachment.
                var scratch = Path.Combine(_directory, "attachment-probe.mkv");
                await RunAsync("ffmpeg", ["-v", "error", "-nostdin", "-y", $"-dump_attachment:{index}", path, "-i", source, "-map", "0:v:0", "-frames:v", "1", "-c", "copy", scratch], ct);
                if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Subtitle font is too large.");
            }
        }
    }

    private static async Task<string> RunAsync(string tool, string[] arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, tool + ".exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"Could not start {tool}.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
            var message = await errors;
            if (process.ExitCode != 0) throw new IOException($"Subtitle preparation failed: {message[..Math.Min(message.Length, 2000)]}");
            return await output;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        catch (IOException) { /* Windows may still be releasing a font handle. */ }
        catch (UnauthorizedAccessException) { }
    }
}
