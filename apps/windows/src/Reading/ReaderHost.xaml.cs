using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.Views;
using Anibel.App.Views.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Anibel.App.Reading;

public sealed partial class ReaderHost : UserControl
{
    private bool _mini;
    private ReaderArgs? _args;
    private double[] _chapters = [];
    private CancellationTokenSource _cts = new();

    public event EventHandler? CollapseRequested;
    public event EventHandler? ExpandRequested;
    public event EventHandler? Closed;
    public event EventHandler? FullscreenRequested;

    public bool IsMini => _mini;
    public bool IsOpen => Visibility == Visibility.Visible;

    public ReaderHost()
    {
        InitializeComponent();
    }

    public async Task OpenAsync(ReaderArgs args)
    {
        Visibility = Visibility.Visible;
        SetMini(false);
        _args = args;
        _chapters = args.Chapters ?? [];
        await LoadChapterAsync(args.Chapter, args.ChapterTitle, args.ChapterId);
    }

    public void SetMini(bool mini, bool externalChrome = false)
    {
        _mini = mini;
        var state = !mini ? "Theater" : externalChrome ? "Pip" : "Mini";
        VisualStateManager.GoToState(this, state, true);
    }

    public void Close()
    {
        CancelInFlight();
        PagesList.ItemsSource = null;
        HideError();
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void SetOsFullscreen(bool fullscreen)
    {
        FullscreenIcon.Symbol = fullscreen ? Symbol.BackToWindow : Symbol.FullScreen;
    }

    private async Task LoadChapterAsync(double chapter, string? title, string? chapterId)
    {
        CancelInFlight();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true, Strings.LoadingChapter);
        try
        {
            if (TryOpenLocal(chapter, title, chapterId))
            {
                return;
            }

            var core = App.Services.GetRequiredService<ICoreClient>();
            var loaded = await core.ChapterAsync(_args!.Slug, chapter, ct);
            ct.ThrowIfCancellationRequested();
            if (loaded is null || loaded.Images.Length == 0)
            {
                PagesList.ItemsSource = null;
                ShowError(Strings.NoPagesTitle, Strings.NoPagesMessage, copy: false);
                return;
            }

            var label = string.IsNullOrWhiteSpace(title)
                ? (string.IsNullOrWhiteSpace(loaded.Title) ? Strings.ChapterTitle(ChapterDisplay.Label(loaded)) : loaded.Title)
                : title;
            TitleText.Text = string.IsNullOrEmpty(_args.MediaTitle)
                ? label
                : $"{_args.MediaTitle} · {label}";
            MiniTitle.Text = TitleText.Text;
            PagesList.ItemsSource = loaded.Images;
            PagesScroll.ChangeView(null, 0, 1, true);
            HideError();
            Overlay.Visibility = Visibility.Collapsed;
            UpdateNav(chapter);
            if (!string.IsNullOrEmpty(chapterId ?? loaded.Id)
                && App.Services.GetRequiredService<SessionService>().HasSession)
            {
                _ = core.AddHistoryRecordAsync(chapterId ?? loaded.Id, "chapter");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowError(Strings.Sorry, ex.Message, copy: true);
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private bool TryOpenLocal(double chapter, string? title, string? chapterId)
    {
        IReadOnlyList<string>? images = null;
        if (_args?.LocalImages is { Count: > 0 }
            && _args.Chapter == chapter)
        {
            images = _args.LocalImages;
        }
        else
        {
            var downloads = App.Services.GetService<DownloadService>();
            var item = downloads?.FindCompletedChapter(_args!.Slug, chapter);
            if (item is { ImagePaths.Length: > 0 })
            {
                images = item.ImagePaths;
                title ??= item.Subtitle;
                chapterId ??= item.ChapterId;
            }
        }
        if (images is null || images.Count == 0)
        {
            return false;
        }

        var pages = images
            .Select(p => new ChapterImageDto { Large = ToImageUri(p) })
            .ToArray();
        var label = string.IsNullOrWhiteSpace(title) ? Strings.ChapterTitle($"{chapter:0.##}") : title;
        TitleText.Text = string.IsNullOrEmpty(_args!.MediaTitle)
            ? label
            : $"{_args.MediaTitle} · {label}";
        MiniTitle.Text = TitleText.Text;
        PagesList.ItemsSource = pages;
        PagesScroll.ChangeView(null, 0, 1, true);
        HideError();
        Overlay.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        UpdateNav(chapter);
        if (!string.IsNullOrEmpty(chapterId)
            && App.Services.GetService<SessionService>()?.HasSession == true)
        {
            var core = App.Services.GetService<ICoreClient>();
            if (core is not null)
            {
                _ = core.AddHistoryRecordAsync(chapterId, "chapter");
            }
        }
        return true;
    }

    private static string ToImageUri(string path)
    {
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        return new Uri(path).AbsoluteUri;
    }

    private void UpdateNav(double current)
    {
        if (_args is not null)
        {
            _args = _args with { Chapter = current };
        }
        var i = Array.FindIndex(_chapters, c => Math.Abs(c - current) < 0.001);
        PrevButton.IsEnabled = i > 0;
        NextButton.IsEnabled = i >= 0 && i < _chapters.Length - 1;
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        var i = Array.FindIndex(_chapters, c => Math.Abs(c - (_args?.Chapter ?? 0)) < 0.001);
        if (i > 0)
        {
            await LoadChapterAsync(_chapters[i - 1], null, null);
        }
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        var i = Array.FindIndex(_chapters, c => Math.Abs(c - (_args?.Chapter ?? 0)) < 0.001);
        if (i >= 0 && i < _chapters.Length - 1)
        {
            await LoadChapterAsync(_chapters[i + 1], null, null);
        }
    }

    private void OnSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_mini)
        {
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke(this, EventArgs.Empty);
    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);
    private void OnFullscreenClick(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void CancelInFlight()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    private void SetBusy(bool busy, string? message = null)
    {
        if (busy)
        {
            HideError();
        }
        LoadingRing.IsActive = busy;
        LoadingRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = message ?? "";
        Overlay.Visibility = busy || !string.IsNullOrEmpty(message) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string title, string message, bool copy)
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        Overlay.Visibility = Visibility.Collapsed;
        ErrorState.Title = title;
        ErrorState.Message = message;
        ErrorState.SecondaryText = copy ? Strings.Copy : "";
        ErrorHost.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorHost.Visibility = Visibility.Collapsed;
    }

    private async void OnRetryErrorClick(object sender, RoutedEventArgs e)
    {
        if (_args is not null)
        {
            await LoadChapterAsync(_args.Chapter, _args.ChapterTitle, _args.ChapterId);
        }
    }

    private void OnCopyErrorClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(ErrorState.Message);
}
