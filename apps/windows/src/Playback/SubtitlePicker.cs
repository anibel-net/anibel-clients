namespace Anibel.App.Playback;

public readonly record struct SubtitleTrackInfo(
    long Id,
    string Title,
    string Lang,
    string FileName);

/// <summary>
/// Dub should never show dialogue subs. Only a signs/forced file (on-screen
/// text) is kept, and only when we can identify it. Sub keeps dialogue.
/// </summary>
public static class SubtitlePicker
{
    public static bool IsSigns(string? name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant().Replace('\\', '/');
        if (n.Length == 0)
        {
            return false;
        }
        if (n.Contains("s&s", StringComparison.Ordinal)
            || n.Contains("signsong", StringComparison.Ordinal)
            || n.Contains("sign_song", StringComparison.Ordinal)
            || n.Contains("signsongs", StringComparison.Ordinal))
        {
            return true;
        }
        return HasToken(n,
            "signs", "sign", "forced",
            "надпісы", "надпіс", "надписи", "надпис",
            "знаки", "знак");
    }

    public static IReadOnlyList<string> Filter(IEnumerable<string> paths, bool preferDub)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0)
        {
            return list;
        }
        if (preferDub)
        {
            return list.Where(IsSigns).ToList();
        }
        var dialogue = list.Where(p => !IsSigns(p)).ToList();
        return dialogue.Count > 0 ? dialogue : list;
    }

    public static long? Pick(IReadOnlyList<SubtitleTrackInfo> tracks, bool preferDub)
    {
        if (tracks.Count == 0)
        {
            return null;
        }
        if (preferDub)
        {
            var signs = tracks
                .Select(t => (t.Id, Score: SignScore(t)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ToList();
            return signs.Count > 0 ? signs[0].Id : null;
        }

        return tracks
            .Select(t => (t.Id, Score: DialogueScore(t)))
            .OrderByDescending(x => x.Score)
            .First().Id;
    }

    public static string FileNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Path.GetFileName(uri.LocalPath);
        }
        return Path.GetFileName(url.Replace('\\', '/'));
    }

    public static string SafeFileName(string? url, string? label, int index)
    {
        var name = FileNameFromUrl(url);
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(label))
        {
            name = label.Trim() + ".ass";
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"sub{index}.ass";
        }
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        if (!name.EndsWith(".ass", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".ssa", StringComparison.OrdinalIgnoreCase))
        {
            name += ".ass";
        }
        return name;
    }

    private static int SignScore(SubtitleTrackInfo t) => IsSigns(Blob(t)) ? 100 : 0;

    private static int DialogueScore(SubtitleTrackInfo t) => IsSigns(Blob(t)) ? 1 : 50;

    private static string Blob(SubtitleTrackInfo t) =>
        $"{t.FileName} {t.Title} {t.Lang}";

    private static bool HasToken(string hay, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            var i = 0;
            while ((i = hay.IndexOf(token, i, StringComparison.Ordinal)) >= 0)
            {
                var before = i == 0 || !char.IsLetterOrDigit(hay[i - 1]);
                var after = i + token.Length >= hay.Length || !char.IsLetterOrDigit(hay[i + token.Length]);
                if (before && after)
                {
                    return true;
                }
                i += token.Length;
            }
        }
        return false;
    }
}
