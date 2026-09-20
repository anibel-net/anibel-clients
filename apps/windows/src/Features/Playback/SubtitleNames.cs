using Anibel.App.Core;
using System.IO;

namespace Anibel.App.Playback;

internal static class SubtitleNames
{
    internal static string ForAsset(string path, IReadOnlyList<SubtitleTrackDto> tracks)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('-', 3);
        if (parts is ["sub", var indexText, var original] && int.TryParse(indexText, out var index))
        {
            if (index >= 0 && index < tracks.Count && !string.IsNullOrWhiteSpace(tracks[index].Label))
                return Uri.UnescapeDataString(tracks[index].Label!);
            name = original;
        }
        return Uri.UnescapeDataString(name);
    }
}
