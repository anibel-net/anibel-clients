using System.Text.Json;

namespace Anibel.App.Core;

/// <summary>Host-facing core contract. ViewModels depend on this, not the FFI type.</summary>
public interface ICoreClient
{
    bool IsConnected { get; }
    Task<T> CallAsync<T>(string op, object? args = null, CancellationToken ct = default);

    Task<CoreVersion> GetVersionAsync();
    Task<List<MediaCard>> SearchAsync(string query, int limit = 10, CancellationToken ct = default);
    Task<List<MediaCard>> TrendsAsync(string type = "all", string date = "week", int limit = 12, CancellationToken ct = default);
    Task<PaginationDto<MediaCard>> MediaListAsync(string mediaType, long offset = 0, int limit = 20, object? filters = null, CancellationToken ct = default);
    Task<PaginationDto<EpisodeDto>> EpisodesAsync(string mediaId, string type = "sub", int resource = 1, int? limit = null, CancellationToken ct = default);
    Task<List<EpisodeDto>> EpisodesMatrixAsync(string mediaId, CancellationToken ct = default);
    Task<PlaybackIntentDto> ResolveEpisodeAsync(string videoIdOrUrl, CancellationToken ct = default);
    Task<MediaDetailDto?> MediaAsync(string slug, string? mediaType = null, CancellationToken ct = default);
    Task<AnibelFiltersDto> FiltersAsync(string mediaType, CancellationToken ct = default);
    Task<PaginationDto<CommentDto>> CommentsAsync(string mediaId, string mediaType, int offset = 0, int limit = 20, CancellationToken ct = default);
    Task<CommentDto> AddCommentAsync(string mediaId, string mediaType, string content, string? replyTo = null, CancellationToken ct = default);
    Task<PaginationDto<ChapterDto>> ChaptersAsync(string mediaId, int? limit = 50, CancellationToken ct = default);
    Task<ChapterDto?> ChapterAsync(string slug, double chapter, CancellationToken ct = default);
    Task<LoginUserDto> LoginAsync(string username, string password, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task SetTokenAsync(string token, CancellationToken ct = default);
    Task MarkAsAsync(string mediaId, string mediaType, string status, CancellationToken ct = default);
    Task RemoveMarkAsync(string mediaId, string mediaType, string status, CancellationToken ct = default);
    Task AddFavoriteAsync(string mediaId, string mediaType, CancellationToken ct = default);
    Task RemoveFavoriteAsync(string mediaId, string mediaType, CancellationToken ct = default);
    Task AddHistoryRecordAsync(string entityId, string type = "episode", CancellationToken ct = default);
    Task RemoveHistoryRecordAsync(string entityId, string type = "episode", CancellationToken ct = default);
    Task<SlideDto[]> SliderAsync(int limit = 6, CancellationToken ct = default);
    Task<MediaCard[]> UpdatesAsync(string type = "ALL", int offset = 0, int limit = 12, CancellationToken ct = default);
    Task<ProfileDto?> UserAsync(string username, CancellationToken ct = default);
    Task<PaginationDto<MediaCard>> FavoritesAsync(string username, string? mediaType = null, int offset = 0, int limit = 60, CancellationToken ct = default);
    Task<PaginationDto<MarkEntryDto>> MarksAsync(string username, string? mediaType = null, int offset = 0, int limit = 60, CancellationToken ct = default);
    Task<StatusCountersDto?> StatusAsync(string username, string mediaType, CancellationToken ct = default);
    IReadOnlyList<JsonElement> DrainEvents();
}
