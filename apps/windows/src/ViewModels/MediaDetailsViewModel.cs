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
    private int _episodeGeneration;
    private bool _updatingEpisodeChoices;
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
    public List<(string Key, string Label)> MarkChoices { get; private set; } = [];
    private async Task ApplyKind(string type)
    {
        var kind = await _core.CallAsync<MediaKindDto>("mediaKind", new { mediaType = type });
        ShowEpisodes = kind.Content == "episodes";
        ShowChapters = kind.Content == "chapters";
        ShowGameInfo = kind.Content == "game";
        ShowBookInfo = kind.Content == "book";
        MarkChoices = kind.Marks.Select(key => (key, MarkLabel(key))).ToList();
        OnPropertyChanged(nameof(MarkChoices));
    }
    private static string MarkLabel(string key) => key switch
    {
        "watching" => Strings.MarkWatching,
        "watched" => Strings.MarkWatched,
        "reading" => Strings.MarkReading,
        "read" => Strings.MarkRead,
        "playing" => Strings.MarkPlaying,
        "played" => Strings.MarkPlayed,
        "planned" => Strings.MarkPlanned,
        "dropped" => Strings.MarkDropped,
        _ => Strings.MarkNotSelected,
    };
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
    [ObservableProperty] private bool isPosting;
    [ObservableProperty] private bool ratingSaving;

    public async Task RefreshPersonalAsync()
    {
        if (Media is not { } media) return;
        media.IRated = null;
        media.Favorite = null;
        media.Mark = null;
        RefreshPersonal();
        OnPropertyChanged(nameof(Media));
        if (!_session.HasSession) return;
        var revision = _session.Revision;
        try
        {
            var refreshed = await _core.MediaAsync(media.Slug, media.MediaType);
            if (ReferenceEquals(Media, media) && revision == _session.Revision)
            {
                media.IRated = refreshed?.IRated;
                media.Favorite = refreshed?.Favorite;
                media.Mark = refreshed?.Mark;
                RefreshPersonal();
                OnPropertyChanged(nameof(Media));
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(Media, media) && revision == _session.Revision)
                StatusMessage = Ui.DisplayMessage(ex);
        }
    }

    public async Task SetRatingAsync(int rating)
    {
        if (RatingSaving || Media is not { } media) return;
        if (!_session.HasSession) { StatusMessage = "Увайдзіце, каб паставіць ацэнку"; return; }
        var revision = _session.Revision;
        RatingSaving = true;
        StatusMessage = null;
        try
        {
            await _core.CallAsync<System.Text.Json.JsonElement>("setRating", new { mediaId = media.MediaId, mediaType = media.MediaType, rating });
            if (revision != _session.Revision) return;
            media.IRated = rating;
            if (ReferenceEquals(Media, media)) OnPropertyChanged(nameof(Media));
        }
        catch (Exception ex) { if (ReferenceEquals(Media, media)) StatusMessage = Ui.DisplayMessage(ex); }
        finally { RatingSaving = false; }
    }

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
        IDisposable? bypass = force ? CoreRequestScope.Reload() : null;
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
            await ApplyKind(loaded.MediaType);
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
        if (IsPosting) return false;
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
        var media = Media;
        var target = ReplyTarget;
        IsPosting = true;
        try
        {
            await _core.AddCommentAsync(media.MediaId, media.MediaType, text, target?.Id);
            if (!ReferenceEquals(Media, media)) return true;
            if (ReferenceEquals(ReplyTarget, target)) CancelReply();
            // The server owns thread placement. Never insert a reply as a new root.
            await LoadCommentsAsync(media.MediaId, media.MediaType);
            return true;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(Media, media)) StatusMessage = Ui.DisplayMessage(ex);
            return false;
        }
        finally { IsPosting = false; }
    }

    public void BeginReply(CommentDto comment)
    {
        if (IsPosting || string.IsNullOrWhiteSpace(comment.Id))
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

    private void RefreshPersonal()
    {
        ShowPersonal = _session.HasSession && Media is not null;
        _suppressMarkEvents = true;
        try
        {
            IsFavorite = ShowPersonal && Media?.Favorite == true;
            var mark = ShowPersonal ? Media?.Mark?.Status ?? "notselected" : "notselected";
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
            await UpdateEpisodeChoicesAsync();
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
            if (Media?.MediaId != mediaId || Media.MediaType != mediaType) return;
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
            if (Media?.MediaId == mediaId && Media.MediaType == mediaType)
            {
                CommentsEmpty = Comments.Count == 0;
                StatusMessage = Ui.DisplayMessage(ex);
            }
        }
        finally
        {
            CommentsLoading = false;
        }
    }

    partial void OnSelectedResourceIndexChanged(int value) { if (!_updatingEpisodeChoices) ApplyEpisodeFilter(); }
    partial void OnSelectedKindChanged(string value) { if (!_updatingEpisodeChoices) ApplyEpisodeFilter(); }
    public void ApplyEpisodeFilter() => _ = UpdateEpisodeChoicesAsync();
    private async Task UpdateEpisodeChoicesAsync()
    {
        if (Media is null) return;
        var generation = ++_episodeGeneration;
        long? resource = SelectedResourceIndex > 0 && SelectedResourceIndex < _resourceValues.Count ? _resourceValues[SelectedResourceIndex] : null;
        try
        {
            var choices = await _core.CallAsync<EpisodeChoicesDto>("episodeChoices", new { mediaId = Media.MediaId, kind = SelectedKind, resource });
            if (generation != _episodeGeneration) return;
            _updatingEpisodeChoices = true;
            try
            {
                SelectedKind = choices.SelectedKind;
                HasDub = choices.Kinds.Contains("dub"); HasSub = choices.Kinds.Contains("sub"); HasOther = choices.Kinds.Contains("other");
                ShowKindBar = choices.Kinds.Length > 1;
                ResourceLabels.Clear(); _resourceValues.Clear();
                ResourceLabels.Add(Strings.AllSources); _resourceValues.Add(0);
                foreach (var value in choices.Resources)
                {
                    _resourceValues.Add(value);
                    ResourceLabels.Add(value == 1 ? Strings.ResourceGoogleDrive : Strings.ResourceAnibelPlayer);
                }
                SelectedResourceIndex = resource is { } selected ? Math.Max(0, _resourceValues.IndexOf(selected)) : 0;
                ShowResourceFilter = choices.Resources.Length > 1;
                Episodes.Clear(); foreach (var episode in choices.Items) Episodes.Add(episode);
                EpisodesEmpty = Episodes.Count == 0;
            }
            finally { _updatingEpisodeChoices = false; }
        }
        catch (Exception ex) { if (generation == _episodeGeneration) StatusMessage = Ui.DisplayMessage(ex); }
    }

    public sealed record SelectionResult(bool Selected);
    public sealed record MarkResult(MarkDto? Mark);

    [RelayCommand]
    public async Task SetFavoriteAsync(bool want)
    {
        if (Media is not { } media) return;
        var revision = _session.Revision;
        try
        {
            var result = await _core.CallAsync<SelectionResult>("setFavorite", new { mediaId = media.MediaId, mediaType = media.MediaType, selected = want });
            if (!ReferenceEquals(Media, media) || revision != _session.Revision) return;
            media.Favorite = result.Selected;
            IsFavorite = result.Selected;
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
        var media = Media;
        var revision = _session.Revision;
        try
        {
            var result = await _core.CallAsync<MarkResult>("setMark", new { mediaId = media.MediaId, mediaType = media.MediaType, status, current = media.Mark?.Status });
            if (!ReferenceEquals(Media, media) || revision != _session.Revision) return;
            media.Mark = result.Mark;
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
            var result = await _core.CallAsync<SelectionResult>("setWatched", new { entityId = ep.Id, selected = ep.Watched != true });
            ep.Watched = result.Selected;
        }
        catch (Exception ex)
        {
            StatusMessage = Ui.DisplayMessage(ex);
        }
    }
}
