using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.Views.Converters;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;

namespace Anibel.App;

/// <summary>
/// The application window: hosts the root frame that navigates to <see cref="MainPage"/>.
/// Title bar follows the Microsoft Store pattern: branding, centered search, account.
/// </summary>
public sealed partial class MainWindow : Window,
    IRecipient<SessionChangedMessage>
{
    public AppUpdateService Updates { get; }
    private ClosePhase _closePhase;

    public async Task RestartForUpdateAsync()
    {
        if (!Updates.IsReady) return;
        var downloads = App.Services.GetRequiredService<DownloadService>();
        await downloads.RefreshAsync();
        if (downloads.Items.Any(item => item.IsActive) || RootFrame.Content is MainPage { HasOpenMedia: true })
            throw new InvalidOperationException("Спачатку закрыйце прайгравальнік і дачакайцеся завяршэння спамповак.");
        var instances = System.Diagnostics.Process.GetProcessesByName("Anibel.Net");
        try
        {
            if (instances.Length > 1) throw new InvalidOperationException("Спачатку закрыйце іншыя вокны Anibel.Net.");
        }
        finally { foreach (var instance in instances) instance.Dispose(); }
        await CloseAsync(restartForUpdate: true);
    }

    private void OnUpdateDetailsClick(object sender, RoutedEventArgs e)
        => WeakReferenceMessenger.Default.Send(new NavigateMessage("settings"));

    private enum ClosePhase { Active, Waiting, Complete }
    private const double TitleVisibleMinWidth = 820;
    private const double SearchMaxWidth = 520;
    private readonly DispatcherQueueTimer _suggestTimer;
    private readonly SearchHistory _searchHistory;
    private CancellationTokenSource _suggestCts = new();
    private string _suggestQuery = "";
    private bool _layoutBusy;
    private bool _removingHistory;
    private bool _focusSearchRequested;

    private readonly ShortcutMap _shortcuts = new();
    private ContentDialog? _shortcutHelp;

    private async void OnWindowKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_shortcutHelp is not null) { _shortcuts.Reset(); return; }
        var shortcut = _shortcuts.Read(e.Key, KeyboardNavigation.Modifiers,
            KeyboardNavigation.IsEditing(WindowRoot.XamlRoot), System.Diagnostics.Stopwatch.GetElapsedTime(0));
        if (shortcut is null) return;
        if (shortcut.Action == ShortcutAction.FocusCards && RootFrame.Content is MainPage mediaPage && mediaPage.HasDockedMedia)
            return;
        e.Handled = true;
        switch (shortcut.Action)
        {
            case ShortcutAction.Help: await ShowShortcutHelpAsync(); break;
            case ShortcutAction.Search:
                _focusSearchRequested = true;
                try { GlobalSearch.Focus(FocusState.Keyboard); }
                finally { _focusSearchRequested = false; }
                break;
            case ShortcutAction.Navigate:
                WeakReferenceMessenger.Default.Send(new NavigateMessage(shortcut.Page!)); break;
            case ShortcutAction.Back:
                WeakReferenceMessenger.Default.Send(new GoBackMessage()); break;
            case ShortcutAction.FocusCards:
                if (RootFrame.Content is MainPage page) page.FocusCards(); break;
        }
    }

    internal async Task ShowShortcutHelpAsync()
    {
        if (_shortcutHelp is not null) return;
        var previous = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(WindowRoot.XamlRoot) as Control;
        var help = new Views.ShortcutHelpView { Width = Math.Clamp(WindowRoot.ActualWidth - 96, 240, 980) };
        _shortcutHelp = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = "Спалучэнні клавіш",
            CloseButtonText = "Закрыць",
            Content = new ScrollViewer
            {
                Content = help, MaxHeight = Math.Max(180, WindowRoot.ActualHeight - 200),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        _shortcutHelp.Resources["ContentDialogMaxWidth"] = 1060.0;
        try { await _shortcutHelp.ShowAsync(); }
        finally
        {
            _shortcutHelp = null;
            _shortcuts.Reset();
            if (previous?.XamlRoot == WindowRoot.XamlRoot) previous.Focus(FocusState.Keyboard);
        }
    }

    public MainWindow()
    {
        Updates = App.Services.GetRequiredService<AppUpdateService>();
        InitializeComponent();
        WindowRoot.AddHandler(UIElement.PointerReleasedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
            {
                if (RootFrame.Content is MainPage page &&
                    page.HandleMouseNavigation(e.GetCurrentPoint(WindowRoot).Properties.PointerUpdateKind)) e.Handled = true;
            }), true);
        ApplyBackground();
        GlobalSearch.AddHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSearchPointerPressed), true);
        Activated += (_, _) => _shortcuts.Reset();
        WindowRoot.SizeChanged += (_, _) =>
        {
            if (_shortcutHelp?.Content is ScrollViewer { Content: Views.ShortcutHelpView help } scroll)
            {
                help.Width = Math.Clamp(WindowRoot.ActualWidth - 96, 240, 980);
                scroll.MaxHeight = Math.Max(180, WindowRoot.ActualHeight - 200);
            }
        };
        Title = "Anibel.Net";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppTitleBar.SizeChanged += (_, _) => LayoutTitleBar();
        SearchHost.SizeChanged += (_, _) => LayoutTitleBar();
        AccountButton.SizeChanged += (_, _) => LayoutTitleBar();
        SizeChanged += (_, _) => LayoutTitleBar();

        _searchHistory = App.Services.GetRequiredService<SearchHistory>();
        _suggestTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _suggestTimer.Interval = TimeSpan.FromMilliseconds(280);
        _suggestTimer.IsRepeating = false;
        _suggestTimer.Tick += (_, _) => _ = SuggestAsync();

        WeakReferenceMessenger.Default.RegisterAll(this);
        AppTitleBar.Loaded += (_, _) =>
        {
            RefreshAccount();
            LayoutTitleBar();
        };
        AppWindow.Closing += async (_, e) =>
        {
            if (_closePhase == ClosePhase.Complete) return;
            e.Cancel = true;
            try { await CloseAsync(); }
            catch (Exception ex) { Diag.Log($"Could not close app: {ex}"); }
        };
        Closed += (_, _) =>
        {
            _suggestTimer.Stop();
            _suggestCts.Cancel();
            _suggestCts.Dispose();
            WeakReferenceMessenger.Default.UnregisterAll(this);
        };

        RootFrame.Navigate(typeof(MainPage));
    }

    private async Task CloseAsync(bool restartForUpdate = false)
    {
        if (_closePhase != ClosePhase.Active) return;
        _closePhase = ClosePhase.Waiting;
        try
        {
            if (RootFrame.Content is MainPage page) await page.ClosePlaybackAsync();
            // Window.Close() does not raise AppWindow.Closing. Start the updater here;
            // it waits for this process to exit. A launch failure leaves services running.
            if (restartForUpdate) Updates.PrepareRestart();
            if (Application.Current is App app) await app.StopAsync();
            _closePhase = ClosePhase.Complete;
            Close();
        }
        catch
        {
            _closePhase = ClosePhase.Active;
            throw; // Settings must show the failure instead of silently exiting.
        }
    }

    public void Receive(SessionChangedMessage message) => RefreshAccount();

    public void SetTitleBarVisible(bool visible)
    {
        AppTitleBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            AppWindow.TitleBar.PreferredHeightOption = visible
                ? TitleBarHeightOption.Tall
                : TitleBarHeightOption.Collapsed;
        }
        catch (ArgumentException)
        {
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        }
        if (visible)
        {
            LayoutTitleBar();
        }
    }

    public void EnterVideoFullscreen()
    {
        SetTitleBarVisible(false);
        try
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        catch (Exception ex)
        {
            Diag.Log($"video fullscreen: {ex.Message}");
        }
    }

    public void ExitVideoFullscreen()
    {
        try
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        }
        catch (Exception ex)
        {
            Diag.Log($"video fullscreen exit: {ex.Message}");
        }
        SetTitleBarVisible(true);
    }

    internal void ApplyBackground()
    {
        var background = App.Services.GetRequiredService<SettingsService>().Background;
        SystemBackdrop = background switch
        {
            WindowBackground.Mica when Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()
                => new Microsoft.UI.Xaml.Media.MicaBackdrop
                {
                    Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt,
                },
            WindowBackground.Acrylic when Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported()
                => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
            _ => null,
        };
        SolidBackground.Visibility = SystemBackdrop is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _focusSearchRequested = true;
        try { GlobalSearch.Focus(FocusState.Pointer); }
        finally { _focusSearchRequested = false; }
    }

    private void OnSearchGettingFocus(UIElement sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs e)
    {
        // A closing player's controls can still be loaded when WinUI moves focus.
        // Only explicit search input, keyboard traversal, or focus within search is allowed.
        if (_focusSearchRequested || KeyboardNavigation.FocusIsWithin(GlobalSearch)
            || (e.FocusState == FocusState.Keyboard && e.Direction is
                Microsoft.UI.Xaml.Input.FocusNavigationDirection.Next or Microsoft.UI.Xaml.Input.FocusNavigationDirection.Previous)) return;
        if (!e.TryCancel() && RootFrame.Content is MainPage page)
            e.TrySetNewFocusedElement(page.ContentFocusTarget);
    }

    private void OnSearchGotFocus(object sender, RoutedEventArgs e)
    {
        if (!KeyboardNavigation.FocusIsWithin(GlobalSearch)) return;
        ShowLocalSuggestions(GlobalSearch.Text);
        var q = GlobalSearch.Text?.Trim() ?? "";
        if (q.Length >= 2)
        {
            _suggestQuery = GlobalSearch.Text ?? q;
            _ = SuggestAsync();
        }
    }

    private void OnSearchLostFocus(object sender, RoutedEventArgs e)
    {
        if (KeyboardNavigation.FocusIsWithin(GlobalSearch)) return;
        CloseSearchSuggestions();
    }

    private void CloseSearchSuggestions()
    {
        _suggestTimer.Stop();
        _suggestCts.Cancel();
        GlobalSearch.IsSuggestionListOpen = false;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }
        _suggestCts.Cancel();
        _suggestQuery = sender.Text ?? "";
        var q = _suggestQuery.Trim();
        ShowLocalSuggestions(_suggestQuery);
        _suggestTimer.Stop();
        if (q.Length >= 2)
        {
            _suggestTimer.Start();
        }
    }

    private void ShowLocalSuggestions(string? text)
    {
        if (!KeyboardNavigation.FocusIsWithin(GlobalSearch)) return;
        var q = (text ?? "").Trim();
        var items = QuickSearch.Build(q, [], _searchHistory.Items);
        GlobalSearch.ItemsSource = items;
        GlobalSearch.IsSuggestionListOpen = items.Count > 0;
    }

    private async Task SuggestAsync()
    {
        var q = _suggestQuery.Trim();
        if (q.Length < 2 || !KeyboardNavigation.FocusIsWithin(GlobalSearch))
        {
            return;
        }

        try
        {
            _suggestCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        _suggestCts.Dispose();
        _suggestCts = new CancellationTokenSource();
        var ct = _suggestCts.Token;

        try
        {
            var core = App.Services.GetRequiredService<ICoreClient>();
            var hits = await core.SearchAsync(q, QuickSearch.FetchCount, ct);
            if (ct.IsCancellationRequested || q != (GlobalSearch.Text ?? "").Trim()
                || !KeyboardNavigation.FocusIsWithin(GlobalSearch))
            {
                return;
            }
            GlobalSearch.ItemsSource = QuickSearch.Build(q, hits, _searchHistory.Items);
            GlobalSearch.IsSuggestionListOpen = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Diag.Log($"quick search: {ex.Message}");
        }
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (_removingHistory)
        {
            return;
        }
        if (args.SelectedItem is SearchSuggestion { IsMore: true })
        {
            sender.Text = _suggestQuery;
        }
        else if (args.SelectedItem is SearchSuggestion { IsHistory: true, Title: { Length: > 0 } history })
        {
            sender.Text = history;
        }
        else if (args.SelectedItem is SearchSuggestion { Media: { } media })
        {
            sender.Text = CardDisplay.Title(media);
        }
    }

    private void OnGlobalSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_removingHistory)
        {
            _removingHistory = false;
            return;
        }
        if (args.ChosenSuggestion is SearchSuggestion chosen)
        {
            CloseSearchSuggestions();
            if (chosen.IsMore)
            {
                OpenFullSearch(chosen.Subtitle.Length > 0 ? chosen.Subtitle : sender.Text);
            }
            else if (chosen.IsHistory)
            {
                OpenFullSearch(chosen.Title);
            }
            else if (chosen.Media is { Slug: { Length: > 0 } slug } media)
            {
                Remember(sender.Text);
                WeakReferenceMessenger.Default.Send(new OpenMediaMessage(slug, media.MediaType));
            }
            return;
        }

        OpenFullSearch(args.QueryText);
    }

    private async void OnRemoveHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string query } || query.Length == 0)
        {
            return;
        }
        _removingHistory = true;
        try { await _searchHistory.Remove(query); }
        catch (Exception ex) { Diag.Log($"search history: {ex.Message}"); }
        ShowLocalSuggestions(GlobalSearch.Text);
        if (GlobalSearch.Text?.Trim().Length >= 2)
        {
            _ = SuggestAsync();
        }
        DispatcherQueue.TryEnqueue(() => _removingHistory = false);
    }

    private void OpenFullSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        Remember(text);
        CloseSearchSuggestions();
        WeakReferenceMessenger.Default.Send(new GlobalSearchMessage(text.Trim()));
    }

    private async void Remember(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { await _searchHistory.Add(text); } catch (Exception ex) { Diag.Log($"search history: {ex.Message}"); }
        }
    }

    private void OnAccountFlyoutOpening(object? sender, object e) => RefreshAccount();

    private void OnFlyoutProfileClick(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        WeakReferenceMessenger.Default.Send(new NavigateMessage("profile"));
    }

    private void OnFlyoutDownloadsClick(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        WeakReferenceMessenger.Default.Send(new NavigateMessage("downloads"));
    }

    private void OnFlyoutSettingsClick(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        WeakReferenceMessenger.Default.Send(new NavigateMessage("settings"));
    }

    private async void OnFlyoutLogoutClick(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        try
        {
            await App.Services.GetRequiredService<SessionService>().LogoutAsync();
        }
        catch
        {
        }
        WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
    }

    private void RefreshAccount()
    {
        var session = App.Services.GetRequiredService<SessionService>();
        var name = string.IsNullOrWhiteSpace(session.Username) ? Strings.Guest : session.Username;
        AccountPicture.DisplayName = name;
        FlyoutPicture.DisplayName = name;
        FlyoutName.Text = name;
        BitmapImage? pic = null;
        if (session.Avatar is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            pic = new BitmapImage(uri);
        }
        AccountPicture.ProfilePicture = pic;
        FlyoutPicture.ProfilePicture = pic;
        var loggedIn = session.HasSession;
        FlyoutSignOut.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        FlyoutSignIn.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LayoutTitleBar()
    {
        if (_layoutBusy || AppTitleBar.XamlRoot is null || AppTitleBar.Visibility != Visibility.Visible)
        {
            return;
        }
        _layoutBusy = true;
        try
        {
            var scale = AppTitleBar.XamlRoot.RasterizationScale;
            LeftPaddingColumn.Width = new GridLength(Math.Max(AppWindow.TitleBar.LeftInset / scale, 4));
            RightPaddingColumn.Width = new GridLength(Math.Max(AppWindow.TitleBar.RightInset / scale, 12));

            var barWidth = AppTitleBar.ActualWidth;
            TitleText.Visibility = barWidth >= TitleVisibleMinWidth ? Visibility.Visible : Visibility.Collapsed;

            var inner = SearchHost.ActualWidth - SearchHost.Padding.Left - SearchHost.Padding.Right;
            if (inner > 0)
            {
                GlobalSearch.Width = Math.Min(SearchMaxWidth, inner);
            }
            GlobalSearch.PlaceholderText = barWidth >= 720
                ? Strings.SearchHintFull
                : Strings.SearchHintShort;

            UpdateTitleBarPassthrough(scale);
        }
        finally
        {
            _layoutBusy = false;
        }
    }

    private void UpdateTitleBarPassthrough(double scale)
    {
        var rects = new List<RectInt32>();
        if (GlobalSearch.ActualWidth > 0)
        {
            rects.Add(ToPassthroughRect(GlobalSearch, scale));
        }
        if (AccountButton.ActualWidth > 0)
        {
            rects.Add(ToPassthroughRect(AccountButton, scale));
        }
        if (rects.Count == 0)
        {
            return;
        }
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
            .SetRegionRects(NonClientRegionKind.Passthrough, [.. rects]);
    }

    private static RectInt32 ToPassthroughRect(FrameworkElement element, double scale)
    {
        var bounds = element.TransformToVisual(null).TransformBounds(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return new RectInt32(
            _X: (int)Math.Round(bounds.X * scale),
            _Y: (int)Math.Round(bounds.Y * scale),
            _Width: (int)Math.Round(bounds.Width * scale),
            _Height: (int)Math.Round(bounds.Height * scale));
    }
}
