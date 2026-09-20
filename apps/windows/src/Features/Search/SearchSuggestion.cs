using Anibel.App.Core;
using Anibel.App.Views.Converters;

namespace Anibel.App.Services;

public sealed class SearchSuggestion
{
    public required string Kind { get; init; }
    public MediaCard? Media { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public string? Poster { get; init; }
    public string Glyph { get; init; } = "\uE81C";
    public bool IsMedia => Kind == "media";
    public bool IsMore => Kind == "more";
    public bool IsHistory => Kind == "history";
    public bool HasPoster => IsMedia && !string.IsNullOrWhiteSpace(Poster);

    public static SearchSuggestion FromMedia(MediaCard media)
    {
        var title = CardDisplay.Title(media);
        return new()
        {
            Kind = "media",
            Media = media,
            Title = string.IsNullOrWhiteSpace(title) ? media.Slug : title,
            Subtitle = TypeLabel(media.MediaType)
                + (media.Year is > 0 ? $" · {media.Year}" : string.Empty),
            Poster = media.Poster,
            Glyph = TypeGlyph(media.MediaType),
        };
    }

    public static SearchSuggestion FromHistory(string query) => new()
    {
        Kind = "history",
        Title = query,
        Glyph = "\uE81C",
    };

    public static SearchSuggestion ShowMore(string query, bool hasHits = true) => new()
    {
        Kind = "more",
        Title = hasHits ? Strings.ShowMoreResults : Strings.SearchFor(query),
        Subtitle = query,
        Glyph = "\uE721",
    };

    public static string TypeLabel(string type) => type switch
    {
        "anime" => Strings.Anime,
        "manga" => Strings.Manga,
        "cinema" => Strings.Cinema,
        "games" => Strings.Games,
        "books" => Strings.Books,
        _ => type,
    };

    public static string TypeGlyph(string type) => type switch
    {
        "manga" or "books" => "\uE8A5",
        "games" => "\uE7FC",
        _ => "\uE7F4",
    };

    public override string ToString() => Title;
}

public static class QuickSearch
{
    public const int PreviewCount = 6;
    public const int FetchCount = 8;
    public const int HistoryPreview = 6;
    public const int HistoryWhenSearching = 3;

    public static List<SearchSuggestion> Build(
        string query,
        IReadOnlyList<MediaCard> hits,
        IReadOnlyList<string>? history = null)
    {
        var q = query.Trim();
        var items = new List<SearchSuggestion>(PreviewCount + HistoryPreview + 1);
        var histCap = q.Length == 0 ? HistoryPreview : HistoryWhenSearching;
        if (history is { Count: > 0 })
        {
            IEnumerable<string> rows = history;
            if (q.Length > 0)
            {
                rows = history.Where(s => s.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            foreach (var h in rows.Take(histCap))
            {
                items.Add(SearchSuggestion.FromHistory(h));
            }
        }
        foreach (var media in hits.Take(PreviewCount))
        {
            items.Add(SearchSuggestion.FromMedia(media));
        }
        if (q.Length >= 2)
        {
            items.Add(SearchSuggestion.ShowMore(q, hits.Count > 0));
        }
        return items;
    }
}
