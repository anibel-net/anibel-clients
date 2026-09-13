using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Anibel.App.Core;

// Mirrors core JSON DTOs (camelCase on the wire, PascalCase in C#).
// Display-only computed props live in Anibel.App.Views.Converters, not here.

public sealed record CoreVersion(string Name, string Version);

// XAML-bound DTOs must be classes with setters (WinUI binding engine sets props).
public sealed class TitleDto
{
    public string? Ru { get; set; }
    public string? Be { get; set; }
    public string? En { get; set; }
    public string[]? Alt { get; set; }
}

public sealed class MediaCard
{
    public string MediaId { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Slug { get; set; } = "";
    public TitleDto? Title { get; set; }
    public string? Poster { get; set; }
    public long? Year { get; set; }
    public double? Rating { get; set; }
    public string[]? Genres { get; set; }
    public string? Status { get; set; }
    public string[]? Language { get; set; }
    public string? UpdateType { get; set; }
    public long? Num { get; set; }
}

public sealed class EpisodeDto : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public double? Episode { get; set; }
    public double? EndEpisode { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Type { get; set; }
    public long? Resource { get; set; }
    public long? Released { get; set; }

    private bool? _watched;
    public bool? Watched
    {
        get => _watched;
        set
        {
            if (_watched == value)
            {
                return;
            }
            _watched = value;
            // x:Bind OneWay on Watched re-evaluates the opacity converter.
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record PaginationDto<T>(T[] Docs, long TotalDocs, long? Limit, long? Offset, long NextOffset = 0, bool HasMore = false);
public sealed record UpdatesPageDto(MediaCard[] Docs, long NextOffset, bool HasMore);

public sealed record SubtitleTrackDto(string Url, string[] Fonts, string? Label);

public sealed record FontAssetDto(string Family, string Url);

public enum PlaybackKind { Native, Embed, PendingNative }

public sealed record PlaybackIntentDto(
    PlaybackKind Kind,
    string? PageUrl,
    string? VideoId,
    string? VideoSrc,
    string? AudioSrc,
    string? SubSrc,
    SubtitleTrackDto[] Subtitles,
    FontAssetDto[] Fonts,
    double? DurationSecs);

public sealed record LoginUserDto(string Id, string Username, string? Email, string? Role, string? Token, string? Avatar);

public sealed record ProfileDto(string Id, string Username, string? Avatar, string? DisplayName, string? Bio, string? Wallpaper);

public sealed record AnibelFiltersDto(
    long[]? Years,
    string[]? Genres,
    string[]? Studios,
    string[]? Types = null);

public sealed class SlideDto
{
    public string? Id { get; set; }
    public TitleDto? Title { get; set; }
    public DescriptionDto? Content { get; set; }
    public string? Link { get; set; }
    public string? Img { get; set; }
}

public sealed class DescriptionDto
{
    public string? Ru { get; set; }
    public string? Be { get; set; }
    public string? En { get; set; }
}

public sealed class MarkDto
{
    public string? Status { get; set; }
}

/// <summary>One entry of `marks(username, …)` — mark + its media.</summary>
public sealed class MarkEntryDto
{
    public string? MarkId { get; set; }
    public string? Status { get; set; }
    public MediaCard? Media { get; set; }
}

/// <summary>Per-status counters from `status(username, mediaType)`.</summary>
public sealed record StatusCountersDto(long? Watching, long? Watched, long? Dropped, long? Planned);

public sealed class MediaDetailDto
{
    public string MediaId { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Slug { get; set; } = "";
    public TitleDto? Title { get; set; }
    public DescriptionDto? Description { get; set; }
    public string? Poster { get; set; }
    public string? Wallpaper { get; set; }
    public string? Studio { get; set; }
    public string? Country { get; set; }
    public string? Status { get; set; }
    public long? Year { get; set; }
    public double? Rating { get; set; }
    public double? IRated { get; set; }
    public string[]? Genres { get; set; }
    public string[]? Language { get; set; }
    public MarkDto? Mark { get; set; }
    public bool? Favorite { get; set; }
    public string? Trailer { get; set; }
    public string? Download { get; set; }
    public DescriptionDto? Instructions { get; set; }
    public string? Franchise { get; set; }
    public MediaCard[] Relations { get; set; } = [];
    public MediaCard[] Recommendations { get; set; } = [];
    public EpisodeDto[] Episodes { get; set; } = [];
    public ChapterDto[] Chapters { get; set; } = [];
}

public sealed class ChapterImageDto
{
    public string Large { get; set; } = "";
    public string? Thumbnail { get; set; }
}

public sealed class ChapterDto
{
    public string Id { get; set; } = "";
    public double Chapter { get; set; }
    public double? EndChapter { get; set; }
    public string? Title { get; set; }
    public long? Released { get; set; }
    public long? View { get; set; }
    public bool? Read { get; set; }
    public ChapterImageDto[] Images { get; set; } = [];
}

public sealed class CommentUserDto
{
    public string Username { get; set; } = "";
    public string? Avatar { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class CommentDto
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public CommentUserDto? User { get; set; }
    public long Created { get; set; }
    public CommentDto[] Replies { get; set; } = [];
}

public sealed record ProfileHubDto(long Favorites, long InProgress, long Done, long Planned, long Dropped);

public sealed record MediaKindDto(string Content, string[] Marks);
public sealed record EpisodeChoicesDto(EpisodeDto[] Items, string[] Kinds, string SelectedKind, long[] Resources);

public sealed record ProfileViewDto(ProfileDto Profile, bool IsOwn);
