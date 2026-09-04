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
    IRecipient<SessionChangedMessage>,
    IRecipient<SessionExpiredMessage>
{
    private const double TitleVisibleMinWidth = 820;
    private const double SearchMaxWidth = 520;
    private readonly DispatcherQueueTimer _suggestTimer;
    private readonly SearchHistory _searchHistory;
    private CancellationTokenSource _suggestCts = new();
    private string _suggestQuery = "";
    private bool _layoutBusy;
    private bool _removingHistory;

    public MainWindow()
    {
        InitializeComponent();
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
        Closed += (_, _) =>
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            Playback.PipWindow.Active?.CloseQuiet();
        };

        RootFrame.Navigate(typeof(MainPage));
    }

    public void Receive(SessionChangedMessage message) => RefreshAccount();
    public void Receive(SessionExpiredMessage message) => RefreshAccount();

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

    private void OnSearchGotFocus(object sender, RoutedEventArgs e)
    {
        ShowLocalSuggestions(GlobalSearch.Text);
        var q = GlobalSearch.Text?.Trim() ?? "";
        if (q.Length >= 2)
        {
            _suggestQuery = GlobalSearch.Text ?? q;
            _ = SuggestAsync();
        }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }
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
        var q = (text ?? "").Trim();
        var items = QuickSearch.Build(q, [], _searchHistory.Items);
        GlobalSearch.ItemsSource = items;
        GlobalSearch.IsSuggestionListOpen = items.Count > 0;
    }

    private async Task SuggestAsync()
    {
        var q = _suggestQuery.Trim();
        if (q.Length < 2)
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
            if (ct.IsCancellationRequested)
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

    private void OnRemoveHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string query } || query.Length == 0)
        {
            return;
        }
        _removingHistory = true;
        _searchHistory.Remove(query);
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
        WeakReferenceMessenger.Default.Send(new GlobalSearchMessage(text.Trim()));
    }

    private void Remember(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            _searchHistory.Add(text);
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
            await App.Services.GetRequiredService<ICoreClient>().LogoutAsync();
        }
        catch
        {
        }
        App.Services.GetRequiredService<SessionService>().Clear();
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
