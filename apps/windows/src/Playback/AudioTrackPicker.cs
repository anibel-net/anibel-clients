namespace Anibel.App.Playback;

public readonly record struct AudioTrackInfo(long Id, string Lang, string Title);

/// <summary>
/// Picks the audio id mpv should use. Dub streams often mux original +
/// Belarusian; default aid is the first (usually Japanese).
/// </summary>
public static class AudioTrackPicker
{
    public static long? Pick(IReadOnlyList<AudioTrackInfo> tracks, bool preferDub)
    {
        if (tracks.Count == 0)
        {
            return null;
        }
        if (tracks.Count == 1)
        {
            return tracks[0].Id;
        }

        var ranked = tracks
            .Select(t => (t.Id, Score: Score(t.Lang, t.Title, preferDub)))
            .OrderByDescending(x => x.Score)
            .ToList();
        if (ranked[0].Score > 0)
        {
            return ranked[0].Id;
        }
        // Untagged mux: dub is almost always the last audio track.
        return preferDub ? tracks[^1].Id : tracks[0].Id;
    }

    public static int Score(string lang, string title, bool preferDub)
    {
        var langN = (lang ?? "").Trim().ToLowerInvariant();
        var titleN = (title ?? "").Trim().ToLowerInvariant();
        if (preferDub)
        {
            if (langN is "be" or "bel" or "be-by" or "be_by")
            {
                return 100;
            }
            if (ContainsAny(titleN, "belarus", "беларус", "беларуск", "бел."))
            {
                return 95;
            }
            if (ContainsAny(titleN, "dub", "дуб", "дубляж"))
            {
                return 70;
            }
            if (langN is "ru" or "rus" or "ru-ru")
            {
                return 20;
            }
            return 0;
        }

        if (langN is "jpn" or "ja" or "jp" || ContainsAny(titleN, "japanese", "япон"))
        {
            return 100;
        }
        return 0;
    }

    private static bool ContainsAny(string hay, params string[] needles) =>
        needles.Any(n => hay.Contains(n, StringComparison.Ordinal));
}
