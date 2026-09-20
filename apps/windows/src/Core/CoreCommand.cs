namespace Anibel.App.Core;

public enum CoreCommand
{
    ContinueEpisode,
    ContinueChapter,
    RememberEpisode,
    AddComment,
    AddFavorite,
    AddHistoryRecord,
    Capabilities,
    Chapter,
    Chapters,
    ClearCache,
    ClearPlaybackAssets,
    Comments,
    DownloadChange,
    DownloadEnqueue,
    DownloadExport,
    Downloads,
    DownloadsResume,
    DownloadsSuspend,
    EpisodeChoices,
    Episodes,
    EpisodesMatrix,
    Favorites,
    Filters,
    FontAssets,
    Health,
    Login,
    Logout,
    MarkAs,
    Marks,
    Me,
    Media,
    MediaKind,
    MediaList,
    PersonalList,
    PlaybackOpen,
    PlaybackReport,
    Profile,
    ProfileHub,
    Random,
    ReaderOpen,
    Recommendations,
    RemoveFavorite,
    RemoveHistoryRecord,
    RemoveMark,
    ResolveEpisode,
    Schedule,
    Search,
    SearchHistory,
    SelectTracks,
    Session,
    SetFavorite,
    SetMark,
    SetRating,
    SetToken,
    SetWatched,
    Slider,
    Statistics,
    Status,
    Trends,
    UpdateProfile,
    Updates,
    UpdatesPage,
    User,
    Version,
    VideoInfo,
    VideoQualities,
}

public static class CoreCommands
{
    public static string WireName(this CoreCommand command)
    {
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        var name = command.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
    public static Task<T> CallAsync<T>(this ICoreClient core, CoreCommand command, object? args = null, CancellationToken ct = default)
        => core.CallAsync<T>(command.WireName(), args, ct);
}
