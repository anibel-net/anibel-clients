using System.Collections.ObjectModel;
using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.Views.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anibel.App.ViewModels;

public partial class MediaDetailsViewModel : ObservableObject
{
    private readonly ICoreClient _core;
    private readonly SessionService _session;
    private List<EpisodeDto> _allEpisodes = [];
    private readonly List<long> _resourceValues = [];
    private bool _suppressMarkEvents;

    public MediaDetailsViewModel(ICoreClient core, SessionService session)
    {
        _core = core;
        _session = session;
    }

    public ObservableCollection<ChapterDto> Chapters { get; } = new();
    public ObservableCollection<CommentDto> Comments { get; } = new();
    public ObservableCollection<string> ResourceLabels { get; } = new();
    public ObservableCollection<EpisodeDto> Episodes { get; } = new();
    public ObservableCollection<MediaCard> Relations { get; } = new();
    public ObservableCollection<MediaCard> Recommendations { get; } = new();

    [ObservableProperty] private MediaDetailDto? media;
    [ObservableProperty] private string displayTitle = "";
    [ObservableProperty] private string meta = "";
    [ObservableProperty] private string genres = "";
    [ObservableProperty] private string rating = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string? posterUrl;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool showEpisodes;
    [ObservableProperty] private bool showChapters;
    [ObservableProperty] private bool showGameInfo;
    [ObservableProperty] private bool showBookInfo;
    [ObservableProperty] private bool showFranchise;
    [ObservableProperty] private bool showRecommendations;
    [ObservableProperty] private string franchiseName = "";
    [ObservableProperty] private string? trailerUrl;
    [ObservableProperty] private string? downloadUrl;
    [ObservableProperty] private string? instructions;
    [ObservableProperty] private string languages = "";
    [ObservableProperty] private string chaptersHeader = Strings.Chapters;
    [ObservableProperty] private bool showPersonal;
    public List<(string Key, string Label)> MarkChoices { get; private set; } = DefaultMarks;

    private static readonly List<(string Key, string Label)> DefaultMarks =
    [
        ("notselected", Strings.MarkNotSelected),
        ("watching", Strings.MarkWatching),
        ("watched", Strings.MarkWatched),
        ("dropped", Strings.MarkDropped),
        ("planned", Strings.MarkPlanned),
    ];

    private void ApplyKind(string type)
    {
        var kind = type.Trim().ToLowerInvariant();
        ShowEpisodes = kind is "anime" or "cinema";
        ShowChapters = kind is "manga";
        ShowGameInfo = kind is "games";
        ShowBookInfo = kind is "books";
        ChaptersHeader = Strings.Chapters;
        MarkChoices = kind switch
        {
            "manga" =>
            [
                ("notselected", Strings.MarkNotSelected),
                ("reading", Strings.MarkReading),
                ("read", Strings.MarkRead),
                ("dropped", Strings.MarkDropped),
                ("planned", Strings.MarkPlanned),
            ],
            "books" =>
            [
                ("notselected", Strings.MarkNotSelected),
                ("reading", Strings.MarkReading),
                ("read", Strings.MarkRead),
                ("dropped", Strings.MarkDropped),
                ("planned", Strings.MarkPlanned),
            ],
            "games" =>
            [
                ("notselected", Strings.MarkNotSelected),
                ("playing", Strings.MarkPlaying),
                ("played", Strings.MarkPlayed),
                ("dropped", Strings.MarkDropped),
                ("planned", Strings.MarkPlanned),
            ],
            _ => DefaultMarks,
        };
        OnPropertyChanged(nameof(MarkChoices));
    }
    [ObservableProperty] private bool isFavorite;
    [ObservableProperty] private int markIndex;
    [ObservableProperty] private int selectedResourceIndex;
    [ObservableProperty] private string selectedKind = "dub";
    [ObservableProperty] private bool hasDub;
    [ObservableProperty] private bool hasSub;
    [ObservableProperty] private bool hasOther;
    [ObservableProperty] private bool showKindBar;
    [ObservableProperty] private bool showResourceFilter;
    [ObservableProperty] private bool episodesEmpty;
    [ObservableProperty] private bool chaptersEmpty;
    [ObservableProperty] private bool commentsEmpty;
    [ObservableProperty] private bool episodesLoading;
    [ObservableProperty] private bool chaptersLoading;
    [ObservableProperty] private bool commentsLoading;
    [ObservableProperty] private CommentDto? replyTarget;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool ShowPageError => !IsBusy && Media is null && HasStatus;
    public bool HasReplyTarget => ReplyTarget is not null;
    public string ReplyLabel => ReplyTarget is null ? "" : Strings.ReplyLabel(CommentDisplay.Username(ReplyTarget));
    partial void OnStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowPageError));
    }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowPageError));
    partial void OnMediaChanged(MediaDetailDto? value) => OnPropertyChanged(nameof(ShowPageError));
    partial void OnReplyTargetChanged(CommentDto? value)
    {
        OnPropertyChanged(nameof(HasReplyTarget));
        OnPropertyChanged(nameof(ReplyLabel));
    }

    public void BeginLoad()
    {
        IsBusy = true;
        StatusMessage = null;
        Media = null;
        ShowEpisodes = false;
        ShowChapters = false;
        ShowGameInfo = false;
        ShowBookInfo = false;
        ShowFranchise = false;
        ShowRecommendations = false;
        CancelReply();
    }

    public async Task LoadAsync(string slug, string mediaType, bool force = false)
    {
        IsBusy = true;
        StatusMessage = null;
        IDisposable? bypass = force ? ApiCache.Bypass() : null;
        try
        {
            var loaded = await _core.MediaAsync(slug, mediaType);
            if (loaded is null)
            {
                StatusMessage = Strings.NoSuchTitle;
                return;
            }
            Media = loaded;
            DisplayTitle = Ui.Title(loaded.Title) is { Length: > 0 } t ? t : slug;
            Meta = string.Join(" · ",
                new[]
                {
                    Ui.MediaType(loaded.MediaType),
                    loaded.Year?.ToString(),
                    loaded.Studio,
                    string.IsNullOrWhiteSpace(loaded.Country) ? null : Ui.Country(loaded.Country),
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
            Genres = Ui.Genres(loaded.Genres);
            Rating = loaded.Rating is > 0 ? Strings.Rating(loaded.Rating.Value) : "";
            Description = Ui.Text(loaded.Description?.Be, loaded.Description?.Ru) ?? "";
            PosterUrl = loaded.Poster;
            ApplyKind(loaded.MediaType);
            TrailerUrl = string.IsNullOrWhiteSpace(loaded.Trailer) ? null : loaded.Trailer;
            DownloadUrl = string.IsNullOrWhiteSpace(loaded.Download) ? null : loaded.Download;
            Instructions = Ui.Text(loaded.Instructions?.Be, loaded.Instructions?.Ru);
            Languages = loaded.Language is { Length: > 0 } langs
                ? string.Join(" · ", langs.Where(x => !string.IsNullOrWhiteSpace(x)))
                : "";
            FranchiseName = loaded.Franchise?.Trim() ?? "";
            FillRelated(Relations, loaded.Relations, loaded.Slug);
            FillRelated(Recommendations, loaded.Recommendations, loaded.Slug);
            ShowFranchise = Relations.Count > 0 || FranchiseName.Length > 0;
            ShowRecommendations = Recommendations.Count > 0;
            RefreshPersonal();
            if (ShowChapters)
            {
                await LoadChaptersAsync(loaded.MediaId);
            }
            else if (ShowEpisodes)
            {
                await LoadEpisodesAsync(loaded.MediaId);
            }
            await LoadCommentsAsync(loaded.MediaId, loaded.MediaType);
        }
        catch (Exception ex)
        {
            StatusMessage = Ui.DisplayMessage(ex);
        }
        finally
        {
            bypass?.Dispose();
            IsBusy = false;
        }
    }

    public async Task<bool> AddCommentAsync(string content)
    {
        if (Media is null || !_session.HasSession)
        {
            StatusMessage = Strings.LoginToComment;
            return false;
        }
        var text = content.Trim();
        if (text.Length == 0)
        {
            return false;
        }
        try
        {
            var added = await _core.AddCommentAsync(Media.MediaId, Media.MediaType, text, ReplyTarget?.Id);
            Comments.Insert(0, added);
            CommentsEmpty = Comments.Count == 0;
            CancelReply();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = Ui.DisplayMessage(ex);
            return false;
        }
    }

    public void BeginReply(CommentDto comment)
    {
        if (string.IsNullOrWhiteSpace(comment.Id))
        {
            return;
        }
        ReplyTarget = comment;
    }

    public void CancelReply() => ReplyTarget = null;

    private static void FillRelated(ObservableCollection<MediaCard> target, MediaCard[]? source, string slug)
    {
        target.Clear();
        if (source is null)
        {
            return;
        }
        foreach (var card in source)
        {
            if (string.IsNullOrWhiteSpace(card.Slug) || string.Equals(card.Slug, slug, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            target.Add(card);
        }
    }

    public void RefreshPersonalState() => RefreshPersonal();

    private void RefreshPersonal()
    {
        ShowPersonal = _session.HasSession && Media is not null;
        if (!ShowPersonal || Media is null)
        {
            return;
        }
        _suppressMarkEvents = true;
        try
        {
            IsFavorite = Media.Favorite == true;
            var mark = Media.Mark?.Status ?? "notselected";
            MarkIndex = Math.Max(0, MarkChoices.FindIndex(m => m.Key == mark));
        }
        finally
        {
            _suppressMarkEvents = false;
        }
    }

    private async Task LoadEpisodesAsync(string mediaId)
    {
        EpisodesLoading = true;
        try
        {
            _allEpisodes = await _core.EpisodesMatrixAsync(mediaId);
            PopulateResources(_allEpisodes);
            ApplyEpisodeFilter();
        }
        catch (Exception ex)
        {
            Diag.Log($"MediaDetailsViewModel: episodes FAILED {ex}");
            EpisodesEmpty = true;
        }
        finally
        {
            EpisodesLoading = false;
        }
    }

    private async Task LoadChaptersAsync(string mediaId)
    {
        ChaptersLoading = true;
        try
        {
            var chapters = await _core.ChaptersAsync(mediaId, 500);
            Chapters.Clear();
            foreach (var c in chapters.Docs)
            {
                Chapters.Add(c);
            }
            ChaptersEmpty = Chapters.Count == 0;
        }
        catch (Exception ex)
        {
            Diag.Log($"MediaDetailsViewModel: chapters FAILED {ex}");
            ChaptersEmpty = true;
        }
        finally
        {
            ChaptersLoading = false;
        }
    }

    private async Task LoadCommentsAsync(string mediaId, string mediaType)
    {
        CommentsLoading = true;
        try
        {
            var comments = await _core.CommentsAsync(mediaId, mediaType, 0, 30);
            Comments.Clear();
            foreach (var c in comments.Docs)
            {
                Comments.Add(c);
            }
            CommentsEmpty = Comments.Count == 0;
        }
        catch (Exception ex)
        {
            Diag.Log($"MediaDetailsViewModel: comments FAILED {ex}");
            CommentsEmpty = true;
        }
        finally
        {
            CommentsLoading = false;
        }
    }

    private void PopulateResources(List<EpisodeDto> episodes)
    {
        ResourceLabels.Clear();
        _resourceValues.Clear();
        ResourceLabels.Add(Strings.AllSources);
        _resourceValues.Add(0);
        foreach (var r in episodes.Select(x => x.Resource).Where(r => r is > 0).Select(r => r!.Value).Distinct().OrderBy(r => r))
        {
            ResourceLabels.Add(r == 1 ? Strings.ResourceGoogleDrive : Strings.ResourceAnibelPlayer);
            _resourceValues.Add(r);
        }
        SelectedResourceIndex = 0;
        ShowResourceFilter = _resourceValues.Count > 2;
    }

    partial void OnSelectedResourceIndexChanged(int value) => ApplyEpisodeFilter();
    partial void OnSelectedKindChanged(string value) => ApplyEpisodeFilter();

    public void ApplyEpisodeFilter()
    {
        var playable = _allEpisodes.Where(x => !string.IsNullOrWhiteSpace(x.Url)).ToList();
        HasDub = playable.Any(x => IsKind(x, "dub"));
        HasSub = playable.Any(x => IsKind(x, "sub"));
        HasOther = playable.Any(x => IsKind(x, "other"));
        var kindCount = (HasDub ? 1 : 0) + (HasSub ? 1 : 0) + (HasOther ? 1 : 0);
        ShowKindBar = kindCount > 1;

        var kind = SelectedKind;
        if (kindCount > 0)
        {
            if (kind == "dub" && !HasDub) kind = HasSub ? "sub" : "other";
            else if (kind == "sub" && !HasSub) kind = HasDub ? "dub" : "other";
            else if (kind == "other" && !HasOther) kind = HasDub ? "dub" : "sub";
            if (!string.Equals(kind, SelectedKind, StringComparison.Ordinal))
            {
                SelectedKind = kind;
                return;
            }
        }

        long? resource = SelectedResourceIndex > 0 && SelectedResourceIndex < _resourceValues.Count
            ? _resourceValues[SelectedResourceIndex]
            : null;
        var filtered = playable
            .Where(x => IsKind(x, kind))
            .Where(x => !resource.HasValue || x.Resource == resource)
            .OrderBy(x => x.Episode)
            .ToList();
        Episodes.Clear();
        foreach (var ep in filtered)
        {
            Episodes.Add(ep);
        }
        EpisodesEmpty = Episodes.Count == 0;
    }

    private static bool IsKind(EpisodeDto ep, string kind)
    {
        var dub = string.Equals(ep.Type, "dub", StringComparison.OrdinalIgnoreCase);
        var sub = string.Equals(ep.Type, "sub", StringComparison.OrdinalIgnoreCase);
        return kind switch
        {
            "dub" => dub,
            "sub" => sub,
            _ => !dub && !sub,
        };
    }

    [RelayCommand]
    public async Task SetFavoriteAsync(bool want)
    {
        if (Media is null)
        {
            return;
        }
        try
        {
            if (want)
            {
                await _core.AddFavoriteAsync(Media.MediaId, Media.MediaType);
            }
            else
            {
                await _core.RemoveFavoriteAsync(Media.MediaId, Media.MediaType);
            }
            // Only reflect success — the page rolls the button back via SyncChrome
            // when the call throws, so a failure never leaves the UI in the new state.
            Media.Favorite = want;
            IsFavorite = want;
        }
        catch (Exception ex)
        {
            Diag.Log($"MediaDetailsViewModel: favorite FAILED {ex}");
            StatusMessage = Ui.DisplayMessage(ex);
        }
    }

    [RelayCommand]
    public async Task ChangeMarkAsync()
    {
        if (_suppressMarkEvents || Media is null || !_session.HasSession)
        {
            return;
        }
        if (MarkChoices.Count == 0)
        {
            return;
        }
        var i = Math.Clamp(MarkIndex, 0, MarkChoices.Count - 1);
        var status = MarkChoices[i].Key;
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }
        var current = string.IsNullOrWhiteSpace(Media.Mark?.Status) ? "notselected" : Media.Mark!.Status!;
        if (string.Equals(status, current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            if (status == "notselected")
            {
                await _core.RemoveMarkAsync(Media.MediaId, Media.MediaType, current);
                Media.Mark = null;
            }
            else
            {
                await _core.MarkAsAsync(Media.MediaId, Media.MediaType, status);
                Media.Mark = new MarkDto { Status = status };
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Ui.DisplayMessage(ex);
        }
    }

    [RelayCommand]
    public async Task ToggleWatchedAsync(EpisodeDto? ep)
    {
        if (ep is null || !_session.HasSession) return;
        try
        {
            if (ep.Watched == true)
            {
                await _core.RemoveHistoryRecordAsync(ep.Id);
                ep.Watched = false;
            }
            else
            {
                await _core.AddHistoryRecordAsync(ep.Id);
                ep.Watched = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Ui.DisplayMessage(ex);
        }
    }
}
