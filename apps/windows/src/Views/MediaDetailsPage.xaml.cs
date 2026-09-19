using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using Anibel.App.Views.Converters;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Anibel.App.Views;

public sealed record MediaDetailsArgs(string Slug, string MediaType);

/// <summary>Which pivot items are visible; value-equality drives RebuildPivots.</summary>
public sealed record PivotKind(bool Episodes, bool Chapters, bool Game, bool Book, bool Franchise, bool Recs);

public sealed partial class MediaDetailsPage : Page, IRecipient<SessionChangedMessage>
{
    private PivotKind? _pivotKind;
    private string? _kindBarItemsSig;
    private string? _appliedPosterUrl;
    private bool _chromeQueued;
    private bool _episodesChromeQueued;
    private bool _syncingMark;
    private MediaDetailsArgs? _nav;
    public MediaDetailsViewModel Vm { get; }

    public MediaDetailsPage()
    {
        Vm = App.Services.GetRequiredService<MediaDetailsViewModel>();
        InitializeComponent();
        ChaptersList.ItemsSource = Vm.Chapters;
        CommentsList.ItemsSource = Vm.Comments;
        EpisodesList.ItemsSource = Vm.Episodes;
        FranchiseList.ItemsSource = Vm.Relations;
        RecsList.ItemsSource = Vm.Recommendations;
        ResourceCombo.ItemsSource = Vm.ResourceLabels;
        LoadingHost.Visibility = Visibility.Visible;
        HeaderBlock.Visibility = Visibility.Collapsed;
        ContentPivot.Visibility = Visibility.Collapsed;
        Vm.PropertyChanged += OnVmPropertyChanged;
        WeakReferenceMessenger.Default.RegisterAll(this);
        Unloaded += (_, _) =>
        {
            Vm.PropertyChanged -= OnVmPropertyChanged;
            WeakReferenceMessenger.Default.UnregisterAll(this);
        };
    }

    public void Receive(SessionChangedMessage message)
    {
        _ = Vm.RefreshPersonalAsync();
        DispatcherQueue.TryEnqueue(SyncChrome);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MediaDetailsArgs { Slug: not null } args)
        {
            _nav = args;
            Vm.BeginLoad();
            SyncChrome();
            await Vm.LoadAsync(args.Slug, args.MediaType);
            SyncChrome();
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaDetailsViewModel.SelectedKind):
            case nameof(MediaDetailsViewModel.EpisodesEmpty):
            case nameof(MediaDetailsViewModel.HasDub):
            case nameof(MediaDetailsViewModel.HasSub):
            case nameof(MediaDetailsViewModel.HasOther):
            case nameof(MediaDetailsViewModel.ShowKindBar):
            case nameof(MediaDetailsViewModel.SelectedResourceIndex):
            case nameof(MediaDetailsViewModel.ShowResourceFilter):
                QueueEpisodesChrome();
                break;
            default:
                QueueChrome();
                break;
        }
    }

    private void QueueChrome()
    {
        if (_chromeQueued)
        {
            return;
        }
        _chromeQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _chromeQueued = false;
            SyncChrome();
        });
    }

    private void QueueEpisodesChrome()
    {
        if (_episodesChromeQueued)
        {
            return;
        }
        _episodesChromeQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _episodesChromeQueued = false;
            SyncEpisodesChrome();
        });
    }

    private void SyncChrome()
    {
        var loading = Vm.IsBusy && Vm.Media is null;
        var pageError = Vm.ShowPageError;
        LoadingHost.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        PageError.Visibility = pageError ? Visibility.Visible : Visibility.Collapsed;
        PageError.Message = Vm.StatusMessage ?? "";
        HeaderBlock.Visibility = loading || pageError ? Visibility.Collapsed : Visibility.Visible;
        ContentPivot.Visibility = loading || pageError ? Visibility.Collapsed : Visibility.Visible;
        if (loading || pageError)
        {
            StatusBar.IsOpen = false;
            return;
        }

        var opacity = Vm.IsBusy ? 0.4 : 1;
        if (PosterHost.Opacity != opacity)
        {
            PosterHost.Opacity = opacity;
        }
        TitleText.Text = Vm.DisplayTitle;
        MetaText.Text = Vm.Meta;
        GenresText.Text = Vm.Genres;
        TitleRating.Value = Vm.ShowPersonal && Vm.Media?.IRated is > 0
            ? RatingDisplay.Stars(Vm.Media.IRated.Value) : -1;
        TitleRating.PlaceholderValue = Vm.Media?.Rating is > 0
            ? RatingDisplay.Stars(Vm.Media.Rating.Value) : -1;
        TitleRating.IsReadOnly = !Vm.ShowPersonal || Vm.RatingSaving;
        TitleRating.Caption = Vm.RatingSaving ? "Захаванне…" : TitleRating.Value > 0
            ? $"Мая адзнака: {TitleRating.Value:0.0}" : Vm.Rating;
        ToolTipService.SetToolTip(TitleRating, string.IsNullOrEmpty(Vm.Rating) ? "Ацаніць" : Vm.Rating);
        DescriptionText.Text = Vm.Description;

        if (Vm.HasStatus)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = Vm.StatusMessage ?? "";
            StatusBar.IsOpen = true;
        }
        else if (StatusBar.Severity == InfoBarSeverity.Error)
        {
            StatusBar.IsOpen = false;
        }

        PersonalPanel.Visibility = Vm.ShowPersonal ? Visibility.Visible : Visibility.Collapsed;
        FavoriteButton.IsChecked = Vm.IsFavorite;
        var restoreMarks = !_syncingMark;
        _syncingMark = true;
        try
        {
            if (MarkCombo.Items.Count != Vm.MarkChoices.Count)
            {
                MarkCombo.Items.Clear();
                foreach (var (_, label) in Vm.MarkChoices)
                {
                    MarkCombo.Items.Add(label);
                }
            }
            if (MarkCombo.SelectedIndex != Vm.MarkIndex)
            {
                MarkCombo.SelectedIndex = Vm.MarkIndex;
            }
        }
        finally
        {
            if (restoreMarks)
            {
                _syncingMark = false;
            }
        }

        RebuildPivots();
        ChaptersPivot.Header = Vm.ChaptersHeader;
        BindLink(TrailerLink, Vm.TrailerUrl);
        BindLink(DownloadLink, Vm.DownloadUrl);
        BindLink(BookDownloadLink, Vm.DownloadUrl);
        GameSaveButton.Visibility = string.IsNullOrEmpty(Vm.DownloadUrl) ? Visibility.Collapsed : Visibility.Visible;
        BookSaveButton.Visibility = string.IsNullOrEmpty(Vm.DownloadUrl) ? Visibility.Collapsed : Visibility.Visible;
        GameLanguages.Text = Vm.Languages;
        GameLanguages.Visibility = string.IsNullOrEmpty(Vm.Languages) ? Visibility.Collapsed : Visibility.Visible;
        GameInstructions.Text = Vm.Instructions ?? "";
        GameInstructions.Visibility = string.IsNullOrEmpty(Vm.Instructions) ? Visibility.Collapsed : Visibility.Visible;
        BookLanguages.Text = Vm.Languages;
        BookLanguages.Visibility = string.IsNullOrEmpty(Vm.Languages) ? Visibility.Collapsed : Visibility.Visible;
        BookInstructions.Text = Vm.Instructions ?? "";
        BookInstructions.Visibility = string.IsNullOrEmpty(Vm.Instructions) ? Visibility.Collapsed : Visibility.Visible;
        GameEmpty.Visibility = TrailerLink.Visibility == Visibility.Collapsed
            && DownloadLink.Visibility == Visibility.Collapsed
            && GameInstructions.Visibility == Visibility.Collapsed
            ? Visibility.Visible : Visibility.Collapsed;
        BookEmpty.Visibility = BookDownloadLink.Visibility == Visibility.Collapsed
            && BookInstructions.Visibility == Visibility.Collapsed
            ? Visibility.Visible : Visibility.Collapsed;

        ChaptersEmpty.Visibility = Vm.ChaptersEmpty ? Visibility.Visible : Visibility.Collapsed;
        ChaptersList.Visibility = Vm.ChaptersEmpty ? Visibility.Collapsed : Visibility.Visible;
        CommentsEmpty.Visibility = Vm.CommentsEmpty ? Visibility.Visible : Visibility.Collapsed;
        CommentsList.Visibility = Vm.CommentsEmpty ? Visibility.Collapsed : Visibility.Visible;
        CommentComposer.Visibility = Vm.ShowPersonal ? Visibility.Visible : Visibility.Collapsed;
        CommentLoginHint.Visibility = Vm.ShowPersonal ? Visibility.Collapsed : Visibility.Visible;
        ReplyChip.Visibility = Vm.HasReplyTarget ? Visibility.Visible : Visibility.Collapsed;
        ReplyChipText.Text = Vm.ReplyLabel + (Vm.ReplyTarget is { } reply ? "\n" + reply.Content : "");
        SendCommentButton.IsEnabled = CommentBox.IsEnabled = !Vm.IsPosting;
        CommentBox.PlaceholderText = Vm.HasReplyTarget
            ? Strings.ReplyPlaceholder(CommentDisplay.Username(Vm.ReplyTarget!))
            : Strings.WriteCommentPlaceholder;
        FranchiseNameText.Text = string.IsNullOrEmpty(Vm.FranchiseName)
            ? Strings.FranchiseFallback
            : Vm.FranchiseName;
        FranchiseNameText.Visibility = Visibility.Visible;
        var hasRelations = Vm.Relations.Count > 0;
        FranchiseEmpty.Visibility = hasRelations ? Visibility.Collapsed : Visibility.Visible;
        FranchiseList.Visibility = hasRelations ? Visibility.Visible : Visibility.Collapsed;

        ChaptersLoadingRing.IsActive = Vm.ChaptersLoading;
        ChaptersLoadingRing.Visibility = Vm.ChaptersLoading ? Visibility.Visible : Visibility.Collapsed;
        CommentsLoadingRing.IsActive = Vm.CommentsLoading;
        CommentsLoadingRing.Visibility = Vm.CommentsLoading ? Visibility.Visible : Visibility.Collapsed;

        ApplyPoster(Vm.PosterUrl);
        SyncEpisodesChrome();
    }

    private void SyncEpisodesChrome()
    {
        EpisodesEmpty.Visibility = Vm.EpisodesEmpty ? Visibility.Visible : Visibility.Collapsed;
        EpisodesList.Visibility = Vm.EpisodesEmpty ? Visibility.Collapsed : Visibility.Visible;
        EpisodesLoadingRing.IsActive = Vm.EpisodesLoading;
        EpisodesLoadingRing.Visibility = Vm.EpisodesLoading ? Visibility.Visible : Visibility.Collapsed;
        ResourceCombo.Visibility = Vm.ShowResourceFilter ? Visibility.Visible : Visibility.Collapsed;
        if (ResourceCombo.SelectedIndex != Vm.SelectedResourceIndex
            && Vm.SelectedResourceIndex >= 0
            && Vm.SelectedResourceIndex < ResourceCombo.Items.Count)
        {
            ResourceCombo.SelectedIndex = Vm.SelectedResourceIndex;
        }
        SyncKindBar();
    }

    private void ApplyPoster(string? url)
    {
        if (string.Equals(url, _appliedPosterUrl, StringComparison.Ordinal))
        {
            return;
        }
        _appliedPosterUrl = url;
        if (url is { Length: > 0 } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            PosterImage.Source = (Microsoft.UI.Xaml.Media.ImageSource)new StringToImageConverter().Convert(url, typeof(Microsoft.UI.Xaml.Media.ImageSource), null!, "");
        }
        else
        {
            PosterImage.Source = null;
        }
    }

    private void RebuildPivots()
    {
        if (Vm.Media is null)
        {
            _pivotKind = null;
            ContentPivot.Items.Clear();
            return;
        }
        var kind = new PivotKind(
            Vm.ShowEpisodes, Vm.ShowChapters, Vm.ShowGameInfo,
            Vm.ShowBookInfo, Vm.ShowFranchise, Vm.ShowRecommendations);
        if (kind == _pivotKind)
        {
            return;
        }
        _pivotKind = kind;
        ContentPivot.Items.Clear();
        if (Vm.ShowEpisodes) ContentPivot.Items.Add(EpisodesPivot);
        if (Vm.ShowChapters) ContentPivot.Items.Add(ChaptersPivot);
        if (Vm.ShowGameInfo) ContentPivot.Items.Add(GamePivot);
        if (Vm.ShowBookInfo) ContentPivot.Items.Add(BookPivot);
        if (Vm.ShowFranchise) ContentPivot.Items.Add(FranchisePivot);
        if (Vm.ShowRecommendations) ContentPivot.Items.Add(RecsPivot);
        ContentPivot.Items.Add(CommentsPivot);
        ContentPivot.SelectedIndex = 0;
    }

    private static void BindLink(HyperlinkButton button, string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            button.NavigateUri = uri;
            button.Visibility = Visibility.Visible;
        }
        else
        {
            button.NavigateUri = null;
            button.Visibility = Visibility.Collapsed;
        }
    }

    private void OnRatingLoaded(object sender, RoutedEventArgs e)
    {
        // Keep the native hit testing and keyboard support, but hold stars at their resting size.
        StopStarGrowth(TitleRating);
    }

    private static void StopStarGrowth(DependencyObject parent)
    {
        if (parent is StackPanel panel &&
            panel.Name is "RatingBackgroundStackPanel" or "RatingForegroundStackPanel")
        {
            foreach (var child in panel.Children)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(child);
                visual.StopAnimation("Scale.X");
                visual.StopAnimation("Scale.Y");
                visual.Scale = new System.Numerics.Vector3(0.5f, 0.5f, 1);
            }
            return;
        }
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            StopStarGrowth(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i));
    }

    private async void OnRatingSelected(RatingControl sender, object args)
    {
        if (!Vm.ShowPersonal || Vm.RatingSaving || !double.IsFinite(sender.Value) || sender.Value <= 0) return;
        var rating = (int)Math.Round(Math.Clamp(sender.Value, 0.5, 5) * 2);
        if (Vm.Media?.IRated == rating) return;
        await Vm.SetRatingAsync(rating);
        SyncChrome();
    }

    private async void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        await Vm.SetFavoriteAsync(FavoriteButton.IsChecked == true);
    }

    private async void OnMarkChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingMark || MarkCombo.SelectedIndex < 0)
        {
            return;
        }
        Vm.MarkIndex = MarkCombo.SelectedIndex;
        await Vm.ChangeMarkAsync();
    }

    private void SyncKindBar()
    {
        var itemsSig = $"{Vm.HasDub}|{Vm.HasSub}|{Vm.HasOther}|{Vm.ShowKindBar}";
        if (itemsSig != _kindBarItemsSig)
        {
            _kindBarItemsSig = itemsSig;
            KindBar.SelectionChanged -= OnKindChanged;
            KindBar.Items.Clear();
            if (Vm.HasDub) KindBar.Items.Add(new SelectorBarItem { Text = Strings.KindDub, Tag = "dub" });
            if (Vm.HasSub) KindBar.Items.Add(new SelectorBarItem { Text = Strings.KindSub, Tag = "sub" });
            if (Vm.HasOther) KindBar.Items.Add(new SelectorBarItem { Text = Strings.KindOther, Tag = "other" });
            KindBar.Visibility = Vm.ShowKindBar ? Visibility.Visible : Visibility.Collapsed;
            KindBar.SelectionChanged += OnKindChanged;
        }

        KindBar.SelectionChanged -= OnKindChanged;
        foreach (var item in KindBar.Items.OfType<SelectorBarItem>())
        {
            if (item.Tag as string == Vm.SelectedKind)
            {
                if (!ReferenceEquals(KindBar.SelectedItem, item))
                {
                    KindBar.SelectedItem = item;
                }
                break;
            }
        }
        KindBar.SelectionChanged += OnKindChanged;
    }

    private void OnKindChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string kind)
        {
            Vm.SelectedKind = kind;
        }
        EpisodesEmpty.Visibility = Vm.EpisodesEmpty ? Visibility.Visible : Visibility.Collapsed;
        EpisodesList.Visibility = Vm.EpisodesEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ResourceCombo.SelectedIndex >= 0)
        {
            Vm.SelectedResourceIndex = ResourceCombo.SelectedIndex;
        }
        EpisodesEmpty.Visibility = Vm.EpisodesEmpty ? Visibility.Visible : Visibility.Collapsed;
        EpisodesList.Visibility = Vm.EpisodesEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnWatchedClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EpisodeDto ep })
        {
            await Vm.ToggleWatchedAsync(ep);
        }
    }

    private void OnEpisodeClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EpisodeDto ep && !string.IsNullOrWhiteSpace(ep.Url))
        {
            PlayEpisode(ep);
        }
    }

    private void PlayEpisode(EpisodeDto ep)
    {
        if (Vm.Media is null) return;
        WeakReferenceMessenger.Default.Send(new PlayEpisodeMessage(new PlayerArgs(
            Vm.Media.Slug, Vm.Media.MediaType,
            ep.Url ?? "",
            Vm.DisplayTitle,
            EpisodeDisplay.Number(ep),
            ep.Id,
            EpisodeType: ep.Type,
            EnglishTitle: Vm.Media.Title?.En)));
    }

    private async void OnDownloadEpisodeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EpisodeDto ep })
        {
            await QueueEpisode(ep, audioOnly: false);
        }
    }

    private async void OnDownloadEpisodeAudioClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EpisodeDto ep })
        {
            await QueueEpisode(ep, audioOnly: true);
        }
    }

    private async void OnDownloadAllEpisodesClick(object sender, RoutedEventArgs e)
    {
        var n = 0;
        foreach (var ep in Vm.Episodes.ToArray())
        {
            if (await QueueEpisode(ep, audioOnly: false, silent: true))
            {
                n++;
            }
        }
        ShowDownloadNote(n == 0
            ? Strings.NoEpisodesToDownload
            : Strings.QueueAdded(n));
    }

    private async void OnDownloadChapterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChapterDto ch })
        {
            await QueueChapter(ch);
        }
    }

    private async void OnDownloadAllChaptersClick(object sender, RoutedEventArgs e)
    {
        var n = 0;
        foreach (var ch in Vm.Chapters.ToArray())
        {
            if (await QueueChapter(ch, silent: true))
            {
                n++;
            }
        }
        ShowDownloadNote(n == 0
            ? Strings.NoChaptersToDownload
            : Strings.QueueAdded(n));
    }

    private async void OnDownloadMediaFileClick(object sender, RoutedEventArgs e)
    {
        if (Vm.Media is null || string.IsNullOrWhiteSpace(Vm.DownloadUrl))
        {
            return;
        }
        try
        {
            await Downloads.EnqueueFile(new FileDownloadRequest
            {
                MediaId = Vm.Media.MediaId,
                MediaType = Vm.Media.MediaType,
                Slug = Vm.Media.Slug,
                Title = Vm.DisplayTitle,
                FileUrl = Vm.DownloadUrl,
                PosterUrl = Vm.PosterUrl,
                Subtitle = Strings.KindFile,
            });
            ShowDownloadNote(Strings.FileAddedToDownloads);
        }
        catch (Exception ex) { ShowDownloadError(ex); }
    }

    private async Task<bool> QueueEpisode(EpisodeDto ep, bool audioOnly, bool silent = false)
    {
        if (Vm.Media is null || string.IsNullOrWhiteSpace(ep.Url) || string.IsNullOrWhiteSpace(ep.Id))
        {
            return false;
        }
        try
        {
            await Downloads.EnqueueEpisode(new EpisodeDownloadRequest
            {
                EpisodeId = ep.Id,
                EpisodeUrl = ep.Url,
                MediaId = Vm.Media.MediaId,
                MediaType = Vm.Media.MediaType,
                Slug = Vm.Media.Slug,
                Title = Vm.DisplayTitle,
                PosterUrl = Vm.PosterUrl,
                EpisodeLabel = EpisodeDisplay.Number(ep),
                EpisodeType = ep.Type,
                AudioOnly = audioOnly,
            });
            if (!silent)
            {
                ShowDownloadNote(audioOnly
                    ? Strings.AudioAddedToDownloads
                    : Strings.EpisodeAddedToDownloads);
            }
            return true;
        }
        catch (Exception ex) { ShowDownloadError(ex); return false; }
    }

    private async Task<bool> QueueChapter(ChapterDto ch, bool silent = false)
    {
        if (Vm.Media is null)
        {
            return false;
        }
        try
        {
            await Downloads.EnqueueChapter(new ChapterDownloadRequest
            {
                MediaId = Vm.Media.MediaId,
                MediaType = Vm.Media.MediaType,
                Slug = Vm.Media.Slug,
                Title = Vm.DisplayTitle,
                Chapter = ch.Chapter,
                ChapterId = ch.Id,
                ChapterTitle = ChapterDisplay.TitleLabel(ch),
                PosterUrl = Vm.PosterUrl,
                ChapterList = Vm.Chapters.Select(c => c.Chapter).ToArray(),
            });
            if (!silent)
            {
                ShowDownloadNote(Strings.ChapterAddedToDownloads);
            }
            return true;
        }
        catch (Exception ex) { ShowDownloadError(ex); return false; }
    }

    private void ShowDownloadError(Exception ex) { StatusBar.Severity = InfoBarSeverity.Error; StatusBar.Message = Ui.DisplayMessage(ex); StatusBar.IsOpen = true; }

    private void ShowDownloadNote(string message)
    {
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private static DownloadService Downloads => App.Services.GetRequiredService<DownloadService>();

    private void OnCopyStatusClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(string.IsNullOrEmpty(Vm.StatusMessage) ? StatusBar.Message : Vm.StatusMessage);

    private async void OnRetryLoadClick(object sender, RoutedEventArgs e)
    {
        if (_nav is null)
        {
            return;
        }
        Vm.BeginLoad();
        SyncChrome();
        await Vm.LoadAsync(_nav.Slug, _nav.MediaType, force: true);
        SyncChrome();
    }

    private static CommentDto? CommentFromSender(object sender)
        => sender is FrameworkElement fe
            ? fe.DataContext as CommentDto ?? fe.Tag as CommentDto
            : null;

    private void OnCopyCommentClick(object sender, RoutedEventArgs e)
    {
        var comment = CommentFromSender(sender);
        if (comment is null)
        {
            return;
        }
        var text = string.IsNullOrWhiteSpace(CommentDisplay.Username(comment))
            ? comment.Content
            : $"{CommentDisplay.Username(comment)}: {comment.Content}";
        Ui.CopyToClipboard(text);
    }

    private void OnThreadReply(object? sender, CommentDto comment)
    {
        Vm.BeginReply(comment);
        ContentPivot.SelectedItem = CommentsPivot;
        SyncChrome();
        if (Vm.ShowPersonal)
        {
            CommentBox.StartBringIntoView();
            CommentBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnCancelReplyClick(object sender, RoutedEventArgs e)
    {
        Vm.CancelReply();
        SyncChrome();
    }

    private async void OnSendCommentClick(object sender, RoutedEventArgs e)
    {
        var text = CommentBox.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            return;
        }
        var media = Vm.Media;
        var posting = Vm.AddCommentAsync(text);
        SyncChrome();
        var ok = await posting;
        if (!ReferenceEquals(media, Vm.Media)) return;
        SyncChrome();
        if (ok)
        {
            if (CommentBox.Text?.Trim() == text) CommentBox.Text = "";
            CommentsEmpty.Visibility = Visibility.Collapsed;
            CommentsList.Visibility = Visibility.Visible;
            SyncChrome();
        }
        else
        {
            SyncChrome();
        }
    }

    private void OnRelatedClick(object? sender, MediaCard card)
    {
        Frame.Navigate(typeof(MediaDetailsPage), new MediaDetailsArgs(card.Slug, card.MediaType));
    }

    private void OnChapterClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ChapterDto ch || Vm.Media is null)
        {
            return;
        }
        var numbers = Vm.Chapters.Select(c => c.Chapter).ToArray();
        WeakReferenceMessenger.Default.Send(new ReadChapterMessage(new ReaderArgs(
            Vm.Media.Slug,
            ch.Chapter,
            Vm.DisplayTitle,
            ch.Title,
            ch.Id,
            numbers)));
    }
}
