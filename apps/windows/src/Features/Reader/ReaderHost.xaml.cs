using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.Views;
using Anibel.App.Views.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Anibel.App.Reading;

public sealed partial class ReaderHost : UserControl
{
    private bool _mini;
    private bool _ready;
    private bool _syncingPages;
    private bool _controlsHovered;
    private int _page;
    private enum ReadingMode { Scroll, Pages }
    private ReadingMode Mode => (ReadingMode)ModeCombo.SelectedIndex;
    private int PageCount => _snapshot?.Chapter.Images.Length ?? 0;
    public bool KeepOverlayVisible => _controlsHovered || ReadingOptions.IsOpen || PagePicker.IsDropDownOpen
        || (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Control { FocusState: FocusState.Keyboard } && KeyboardNavigation.FocusIsWithin(FullBar));
    private bool _externalChrome;
    private ReaderArgs? _args;
    private ReaderSnapshot? _snapshot;
    public sealed record ReaderSnapshot(ChapterDto Chapter, double? Previous, double? Next);
    private CancellationTokenSource? _cts;

    public event EventHandler? CollapseRequested;
    public event EventHandler? ExpandRequested;
    public event EventHandler? Closed;
    public event EventHandler? FullscreenRequested;

    public bool IsMini => _mini;
    public bool IsOpen => Visibility == Visibility.Visible;

    public ReaderHost()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, new KeyEventHandler(OnReaderKeyDown), true);
        ContentRoot.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnReaderWheel), true);
        _ready = true;
        Loaded += (_, _) => { SetMini(_mini, _externalChrome); ResizePages(); };
    }

    public async Task OpenAsync(ReaderArgs args)
    {
        Visibility = Visibility.Visible;
        _args = args;
        await LoadChapterAsync(args.Chapter, args.ChapterTitle);
    }

    public void SetMini(bool mini, bool externalChrome = false)
    {
        _mini = mini;
        _externalChrome = externalChrome;
        _controlsHovered = false;
        ReadingOptions.Hide();
        WindowActions.Visibility = externalChrome ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(FullBar, externalChrome ? 0 : 1);
        FullBar.VerticalAlignment = externalChrome ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        SetOverlayVisible(!externalChrome);
        QueuePage(_page);
    }

    public void Close()
    {
        CancelInFlight();
        ReadingOptions.Hide();
        PagesList.ItemsSource = null;
        SinglePage.Source = null;
        _snapshot = null;
        _args = null;
        SetBusy(false);
        HideError();
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void SetOsFullscreen(bool fullscreen)
    {
        FullscreenIcon.Symbol = fullscreen ? Symbol.BackToWindow : Symbol.FullScreen;
    }

    private async Task LoadChapterAsync(double chapter, string? title)
    {
        CancelInFlight();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _args = _args! with { Chapter = chapter, ChapterTitle = title };
        PrevButton.IsEnabled = NextButton.IsEnabled = false;
        _snapshot = null;
        _page = 0;
        PagesList.ItemsSource = null;
        SinglePage.Source = null;
        SyncPageControls();
        SetBusy(true, Strings.LoadingChapter);
        try
        {
            var core = App.Services.GetRequiredService<ICoreClient>();
            var snapshot = await core.CallAsync<ReaderSnapshot>(CoreCommand.ReaderOpen, new { slug = _args!.Slug, chapter, chapters = _args.Chapters }, ct);
            ct.ThrowIfCancellationRequested();
            _snapshot = snapshot;
            var loaded = snapshot.Chapter;
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
            PagesList.ItemsSource = loaded.Images;
            _syncingPages = true;
            PagePicker.ItemsSource = Enumerable.Range(1, loaded.Images.Length).Select(n => $"{n} / {loaded.Images.Length}").ToArray();
            _syncingPages = false;
            RenderPages();
            PagesScroll.ChangeView(0, 0, (float)ZoomSlider.Value, true);
            SingleScroll.ChangeView(0, 0, (float)ZoomSlider.Value, true);
            HideError();
            Overlay.Visibility = Visibility.Collapsed;
            UpdateNav(chapter);

        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) ShowError(Strings.Sorry, ex.Message, copy: true);
        }
        finally
        {
            if (!ct.IsCancellationRequested) { LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed; }
        }
    }

    private void UpdateNav(double current)
    {
        if (_args is not null)
        {
            _args = _args with { Chapter = current };
        }
        PrevButton.IsEnabled = _snapshot?.Previous is not null;
        NextButton.IsEnabled = _snapshot?.Next is not null;
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (_snapshot?.Previous is { } previous) await LoadChapterAsync(previous, null);
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_snapshot?.Next is { } next) await LoadChapterAsync(next, null);
    }

    public void SetOverlayVisible(bool visible)
    {
        if (!_externalChrome) visible = true;
        FullBar.Opacity = visible ? 1 : 0;
        FullBar.IsHitTestVisible = visible;
    }

    private void OnControlsEntered(object sender, PointerRoutedEventArgs e) => _controlsHovered = true;
    private void OnControlsExited(object sender, PointerRoutedEventArgs e) => _controlsHovered = false;

    private void SyncPageControls()
    {
        if (!_ready) return;
        _syncingPages = true;
        PagePicker.IsEnabled = PageCount > 0;
        PagePicker.SelectedIndex = PageCount > 0 ? _page : -1;
        PreviousPageButton.IsEnabled = _page > 0;
        NextPageButton.IsEnabled = _page < PageCount - 1;
        PreviousPageIcon.Symbol = RightToLeft.IsOn ? Symbol.Forward : Symbol.Back;
        NextPageIcon.Symbol = RightToLeft.IsOn ? Symbol.Back : Symbol.Forward;
        _syncingPages = false;
    }

    private void ResizePages()
    {
        if (!_ready || ContentRoot.ActualWidth <= 0) return;
        PagesList.Width = Math.Max(1, ContentRoot.ActualWidth - 16);
        SinglePage.Width = Math.Max(1, ContentRoot.ActualWidth - 16);
        SinglePage.Height = FitCombo.SelectedIndex == 0 ? Math.Max(1, ContentRoot.ActualHeight - 8) : double.NaN;
    }

    private void RenderPages()
    {
        if (!_ready) return;
        var images = Mode == ReadingMode.Scroll ? _snapshot?.Chapter.Images : null;
        if (!ReferenceEquals(PagesList.ItemsSource, images)) PagesList.ItemsSource = images;
        PagesScroll.Visibility = Mode == ReadingMode.Scroll ? Visibility.Visible : Visibility.Collapsed;
        SingleScroll.Visibility = Mode == ReadingMode.Pages ? Visibility.Visible : Visibility.Collapsed;
        FitCombo.IsEnabled = RightToLeft.IsEnabled = Mode == ReadingMode.Pages;
        ResizePages();
        if (PageCount > 0)
        {
            _page = ReaderPages.Clamp(_page, PageCount);
            SinglePage.Source = Mode == ReadingMode.Pages
                ? (ImageSource?)new StringToImageConverter().Convert(_snapshot!.Chapter.Images[_page].Large, typeof(ImageSource), "width:1600", "") : null;
        }
        SyncPageControls();
    }

    private void GoToPage(int page)
    {
        if (PageCount == 0) return;
        _page = ReaderPages.Clamp(page, PageCount);
        RenderPages();
        if (Mode == ReadingMode.Pages) SingleScroll.ChangeView(0, 0, null, true);
        else
        {
            PagesList.UpdateLayout();
            if (PagesList.ContainerFromIndex(_page) is FrameworkElement target)
                target.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
        }
    }

    private void OnReadingOptionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var page = _page;
        RenderPages();
        QueuePage(page);
    }
    private void QueuePage(int page)
    {
        var chapter = _snapshot;
        DispatcherQueue.TryEnqueue(() => { if (IsOpen && ReferenceEquals(chapter, _snapshot)) GoToPage(page); });
    }
    private void OnDirectionChanged(object sender, RoutedEventArgs e) => SyncPageControls();
    private void OnViewportChanged(object sender, SizeChangedEventArgs e) => ResizePages();
    private void OnPageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingPages && PagePicker.SelectedIndex >= 0) GoToPage(PagePicker.SelectedIndex);
    }
    private void OnPreviousPageClick(object sender, RoutedEventArgs e) => GoToPage(ReaderPages.Move(_page, -1, PageCount));
    private void OnNextPageClick(object sender, RoutedEventArgs e) => GoToPage(ReaderPages.Move(_page, 1, PageCount));
    private void OnScrollChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!_ready) return;
        var active = Mode == ReadingMode.Scroll ? PagesScroll : SingleScroll;
        if (!e.IsIntermediate && ReferenceEquals(sender, active) && Math.Abs(ZoomSlider.Value - active.ZoomFactor) > 0.01)
            ZoomSlider.Value = active.ZoomFactor;
        if (_syncingPages || Mode != ReadingMode.Scroll || PageCount == 0) return;
        // Select the page at the top of the viewport, including after wheel/touch scrolling.
        for (var i = 0; i < PageCount; i++)
        {
            if (PagesList.ContainerFromIndex(i) is not FrameworkElement item || item.ActualHeight <= 0) continue;
            var bounds = item.TransformToVisual(PagesScroll).TransformBounds(new Windows.Foundation.Rect(0, 0, item.ActualWidth, item.ActualHeight));
            if (bounds.Bottom > 1) { _page = i; SyncPageControls(); break; }
        }
    }
    private void OnZoomChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        PagesScroll.ChangeView(null, null, (float)e.NewValue);
        SingleScroll.ChangeView(null, null, (float)e.NewValue);
    }
    private void OnResetZoomClick(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 1;
        PagesScroll.ChangeView(0, null, 1);
        SingleScroll.ChangeView(0, null, 1);
    }
    private void OnReaderKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (XamlRoot is null || KeyboardNavigation.IsEditing(XamlRoot)
            || KeyboardNavigation.Modifiers != VirtualKeyModifiers.None || ReadingOptions.IsOpen) return;
        if (FocusManager.GetFocusedElement(XamlRoot) is Button or Slider) return;
        if (Mode == ReadingMode.Scroll && e.Key is VirtualKey.PageUp or VirtualKey.PageDown) return;
        var next = ReaderPages.KeyPage(e.Key, _page, PageCount, Mode == ReadingMode.Pages && RightToLeft.IsOn);
        if (next is null) return;
        e.Handled = true;
        GoToPage(next.Value);
    }

    private void OnReaderWheel(object sender, PointerRoutedEventArgs e)
    {
        if (Mode != ReadingMode.Pages || FitCombo.SelectedIndex != 0 || SingleScroll.ZoomFactor > 1
            || KeyboardNavigation.Modifiers != VirtualKeyModifiers.None) return;
        var delta = e.GetCurrentPoint(ContentRoot).Properties.MouseWheelDelta;
        if (delta == 0) return;
        e.Handled = true;
        GoToPage(ReaderPages.Move(_page, delta < 0 ? 1 : -1, PageCount));
    }

    private void OnSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_mini && !_externalChrome)
        {
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e)
    {
        if (_externalChrome) ExpandRequested?.Invoke(this, EventArgs.Empty);
        else CollapseRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);
    private void OnFullscreenClick(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void CancelInFlight()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
        _cts?.Dispose();
        _cts = null;
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
            await LoadChapterAsync(_args.Chapter, _args.ChapterTitle);
        }
    }

    private void OnCopyErrorClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(ErrorState.Message);
}
