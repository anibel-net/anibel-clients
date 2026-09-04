using System.Text.Json;
using Anibel.App.Core;

namespace Anibel.App.Tests;

public class FakeCoreClient : ICoreClient
{
    public bool IsConnected { get; set; } = true;
    public List<MediaCard> SearchResults { get; set; } = [];
    public List<MediaCard> Trends { get; set; } = [];
    public MediaCard[] Updates { get; set; } = [];
    public SlideDto[] Slides { get; set; } = [];
    public PaginationDto<MediaCard> MediaList { get; set; } = new([], 0, 0, 0);
    public MediaDetailDto? Media { get; set; }
    public AnibelFiltersDto Filters { get; set; } = new([], [], []);
    public PaginationDto<CommentDto> Comments { get; set; } = new([], 0, 0, 0);
    public PaginationDto<ChapterDto> Chapters { get; set; } = new([], 0, 0, 0);
    public List<EpisodeDto> Episodes { get; set; } = [];
    public LoginUserDto? LoginUser { get; set; }
    public Exception? SearchError { get; set; }
    public Exception? MediaListError { get; set; }
    public int SearchCalls { get; private set; }
    public int MediaListCalls { get; private set; }
    public List<string> SearchQueries { get; } = [];

    public Task<CoreVersion> GetVersionAsync() => Task.FromResult(new CoreVersion("anibel-core", "0.1.0"));
    public Task<List<MediaCard>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
    {
        SearchCalls++;
        SearchQueries.Add(query);
        if (SearchError is not null) return Task.FromException<List<MediaCard>>(SearchError);
        return Task.FromResult(SearchResults);
    }
    public Task<List<MediaCard>> TrendsAsync(string type = "all", string date = "week", int limit = 12, CancellationToken ct = default)
        => Task.FromResult(Trends);
    public int LastMediaListOffset { get; private set; }
    public int LastMediaListLimit { get; private set; }
    public int MediaListDelayMs { get; set; }
    public Task<PaginationDto<MediaCard>> MediaListAsync(string mediaType, int offset = 0, int limit = 20, object? filters = null, CancellationToken ct = default)
    {
        MediaListCalls++;
        LastMediaListOffset = offset;
        LastMediaListLimit = limit;
        if (MediaListError is not null)
        {
            return Task.FromException<PaginationDto<MediaCard>>(MediaListError);
        }
        if (MediaListDelayMs <= 0)
        {
            return Task.FromResult(MediaList);
        }
        return DelayMediaListAsync(ct);
    }
    private async Task<PaginationDto<MediaCard>> DelayMediaListAsync(CancellationToken ct)
    {
        await Task.Delay(MediaListDelayMs, ct);
        return MediaList;
    }
    public Task<PaginationDto<EpisodeDto>> EpisodesAsync(string mediaId, string type = "sub", int resource = 1, int? limit = null, CancellationToken ct = default)
        => Task.FromResult(new PaginationDto<EpisodeDto>([.. Episodes], Episodes.Count, limit, 0));
    public Task<List<EpisodeDto>> EpisodesMatrixAsync(string mediaId, CancellationToken ct = default)
        => Task.FromResult(Episodes);
    public Task<PlaybackIntentDto> ResolveEpisodeAsync(string videoIdOrUrl, CancellationToken ct = default)
        => Task.FromResult(new PlaybackIntentDto("native", null, null, "https://n/x", null, null, [], [], 1));
    public Task<MediaDetailDto?> MediaAsync(string slug, string? mediaType = null, CancellationToken ct = default)
        => Task.FromResult(Media);
    public Task<AnibelFiltersDto> FiltersAsync(string mediaType, CancellationToken ct = default)
        => Task.FromResult(Filters);
    public Task<PaginationDto<CommentDto>> CommentsAsync(string mediaId, string mediaType, int offset = 0, int limit = 20, CancellationToken ct = default)
        => Task.FromResult(Comments);
    public string? LastAddedCommentContent { get; private set; }
    public string? LastAddedCommentReplyTo { get; private set; }
    public Task<CommentDto> AddCommentAsync(string mediaId, string mediaType, string content, string? replyTo = null, CancellationToken ct = default)
    {
        LastAddedCommentContent = content;
        LastAddedCommentReplyTo = replyTo;
        return Task.FromResult(new CommentDto { Id = "new", Content = content });
    }
    public Task<PaginationDto<ChapterDto>> ChaptersAsync(string mediaId, int? limit = 50, CancellationToken ct = default)
        => Task.FromResult(Chapters);
    public ChapterDto? Chapter { get; set; }
    public Task<ChapterDto?> ChapterAsync(string slug, double chapter, CancellationToken ct = default)
        => Task.FromResult(Chapter);
    public Task<LoginUserDto> LoginAsync(string username, string password, CancellationToken ct = default)
        => Task.FromResult(LoginUser ?? new LoginUserDto("1", username, null, "user", "tok", null));
    public Task LogoutAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SetTokenAsync(string token, CancellationToken ct = default) => Task.CompletedTask;
    public string? LastMarkAsStatus { get; private set; }
    public string? LastRemoveMarkStatus { get; private set; }
    public Task MarkAsAsync(string mediaId, string mediaType, string status, CancellationToken ct = default)
    {
        LastMarkAsStatus = status;
        return Task.CompletedTask;
    }
    public Task RemoveMarkAsync(string mediaId, string mediaType, string status, CancellationToken ct = default)
    {
        LastRemoveMarkStatus = status;
        return Task.CompletedTask;
    }
    public Task AddFavoriteAsync(string mediaId, string mediaType, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveFavoriteAsync(string mediaId, string mediaType, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddHistoryRecordAsync(string entityId, string type = "episode", CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveHistoryRecordAsync(string entityId, string type = "episode", CancellationToken ct = default) => Task.CompletedTask;
    public virtual Task<SlideDto[]> SliderAsync(int limit = 6, CancellationToken ct = default) => Task.FromResult(Slides);
    public string LastUpdatesType { get; private set; } = "ALL";
    public int LastUpdatesOffset { get; private set; }
    public Task<MediaCard[]> UpdatesAsync(string type = "ALL", int offset = 0, int limit = 12, CancellationToken ct = default)
    {
        LastUpdatesType = type;
        LastUpdatesOffset = offset;
        return Task.FromResult(Updates);
    }
    public Task<ProfileDto?> UserAsync(string username, CancellationToken ct = default) => Task.FromResult<ProfileDto?>(null);
    public PaginationDto<MediaCard> FavoritesPage { get; set; } = new([], 0, 0, 0);
    public PaginationDto<MarkEntryDto> MarksPage { get; set; } = new([], 0, 0, 0);
    public Task<PaginationDto<MediaCard>> FavoritesAsync(string username, string? mediaType = null, int offset = 0, int limit = 60, CancellationToken ct = default)
        => Task.FromResult(FavoritesPage);
    public Task<PaginationDto<MarkEntryDto>> MarksAsync(string username, string? mediaType = null, int offset = 0, int limit = 60, CancellationToken ct = default)
        => Task.FromResult(MarksPage);
    public Task<StatusCountersDto?> StatusAsync(string username, string mediaType, CancellationToken ct = default)
        => Task.FromResult<StatusCountersDto?>(null);
    public IReadOnlyList<JsonElement> DrainEvents() => [];
}
