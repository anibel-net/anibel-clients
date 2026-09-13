using Anibel.App.Playback;
using Anibel.App.Reading;
using Anibel.App.Services;
using Anibel.App.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App;

public sealed partial class MainPage : Page,
    IRecipient<GlobalSearchMessage>,
    IRecipient<OpenMediaMessage>,
    IRecipient<OpenProfileMessage>,
    IRecipient<NavigateMessage>,
    IRecipient<PlayEpisodeMessage>,
    IRecipient<ReadChapterMessage>,
    IRecipient<GoBackMessage>
{
    private static readonly HashSet<string> CatalogTypes = new()
    {
        "anime", "manga", "cinema", "games", "books",
    };

    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["home"] = typeof(HomePage),
        ["search"] = typeof(SearchPage),
        ["profile"] = typeof(ProfilePage),
        ["downloads"] = typeof(DownloadsPage),
        ["settings"] = typeof(SettingsPage),
    };

    private string? _navTag;
    private bool _paneWasOpen = true;
    private bool _inAppFullscreen;
    // This collection owns native views/windows only. Rust owns media and playback state.
    private sealed class OpenTitle(string kind, string slug, string title, FrameworkElement host)
    {
        public string Kind { get; } = kind;
        public string Slug { get; } = slug;
        public string Title { get; } = title;
        public FrameworkElement Host { get; } = host;
        public PipWindow? Window { get; set; }
    }

    private readonly List<OpenTitle> _titles = [];
    private Task _pendingClose = Task.CompletedTask;
    private bool _closing;
    private OpenTitle? DockedTitle => _titles.FirstOrDefault(t => ReferenceEquals(t.Host.Parent, PlayerLayer));

    public MainPage()
    {
        InitializeComponent();
        Diag.Log("MainPage: ctor ok");
        WeakReferenceMessenger.Default.RegisterAll(this);
        Loaded += (_, _) =>
        {
            Diag.Log("MainPage: Loaded -> navigate home");
            Nav.SelectedItem = Nav.MenuItems[0];
            NavigateTo("home");
        };
        KeyDown += OnPageKeyDown;
        Unloaded += (_, _) =>
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            Diag.Log("MainPage: Unloaded");
        };
    }

    private async void OnShortcutHelpClick(object sender, RoutedEventArgs e)
    {
        if (App.CurrentWindow is MainWindow window) await window.ShowShortcutHelpAsync();
    }

    internal bool HasDockedMedia => DockedTitle is not null;

    internal void FocusCards()
    {
        if (ContentFrame.Content is DependencyObject content)
            KeyboardNavigation.Find<MediaCollectionView>(content)?.FocusFirstCard();
    }

    public async Task ClosePlaybackAsync()
    {
        _closing = true;
        foreach (var title in _titles.ToArray()) CloseTitle(title);
        await _pendingClose;
    }

    public void Receive(GlobalSearchMessage message) => RunGlobalSearch(message.Query);
    public void Receive(OpenProfileMessage message)
    {
        ShowMini();
        _navTag = null;
        ContentFrame.Navigate(typeof(ProfilePage), new ProfileArgs(message.Username));
        SyncBack();
    }
    public void Receive(OpenMediaMessage message) => RunOpenMedia(message.Slug, message.MediaType);
    public void Receive(NavigateMessage message) => RunNavigate(message.Tag);
    public void Receive(PlayEpisodeMessage message) => _ = PlayEpisodeAsync(message.Args);
    public void Receive(ReadChapterMessage message) => _ = ReadChapterAsync(message.Args);
    public void Receive(GoBackMessage message) => OnBackRequested(Nav, null!);

    private async Task PlayEpisodeAsync(PlayerArgs args)
    {
        if (_closing) return;
        var title = FindTitle(args.MediaType, args.Slug)
            ?? AddTitle(args.MediaType, args.Slug, args.TitleLabel, new PlayerHost());
        ShowTheater(title);
        await ((PlayerHost)title.Host).PlayAsync(args);
    }

    private async Task ReadChapterAsync(ReaderArgs args)
    {
        if (_closing) return;
        var title = FindTitle("manga", args.Slug)
            ?? AddTitle("manga", args.Slug, args.MediaTitle, new ReaderHost());
        ShowTheater(title);
        await ((ReaderHost)title.Host).OpenAsync(args);
    }

    private OpenTitle? FindTitle(string kind, string slug) =>
        _titles.FirstOrDefault(t => t.Kind == kind && t.Slug == slug);

    private OpenTitle AddTitle(string kind, string slug, string label, FrameworkElement host)
    {
        // Move the current view before adding another docked view.
        ShowMini();
        var title = new OpenTitle(kind, slug, label, host);
        _titles.Add(title);
        if (host is PlayerHost player)
        {
            player.CollapseRequested += (_, _) => MiniHost(title);
            player.ExpandRequested += (_, _) => ShowTheater(title);
            player.FullscreenRequested += (_, _) => ToggleFullscreen(title);
            player.Closed += (_, _) => RemoveTitle(title);
        }
        else if (host is ReaderHost reader)
        {
            reader.CollapseRequested += (_, _) => MiniHost(title);
            reader.ExpandRequested += (_, _) => ShowTheater(title);
            reader.FullscreenRequested += (_, _) => ToggleFullscreen(title);
            reader.Closed += (_, _) => RemoveTitle(title);
        }
        return title;
    }

    private static void SetMini(OpenTitle title, bool mini)
    {
        if (title.Host is PlayerHost player) player.SetMini(mini, externalChrome: mini);
        if (title.Host is ReaderHost reader) reader.SetMini(mini, externalChrome: mini);
    }

    private void ShowTheater(OpenTitle title)
    {
        if (_closing || !_titles.Contains(title)) return;
        if (DockedTitle is { } current && current != title) MiniHost(current);
        CloseWindow(title);
        if (!_inAppFullscreen)
        {
            if (PlayerLayer.Children.Count == 0) _paneWasOpen = Nav.IsPaneOpen;
            Nav.IsPaneOpen = false;
        }
        if (title.Host.Parent is Panel parent) parent.Children.Remove(title.Host);
        PlayerLayer.Children.Add(title.Host);
        title.Host.Visibility = Visibility.Visible;
        title.Host.Width = double.NaN;
        title.Host.Height = double.NaN;
        title.Host.Margin = new Thickness(0);
        title.Host.HorizontalAlignment = HorizontalAlignment.Stretch;
        title.Host.VerticalAlignment = VerticalAlignment.Stretch;
        SetMini(title, false);
        SyncOverlayLayer();
    }

    private void MiniHost(OpenTitle title)
    {
        if (_closing || title.Window is not null || !_titles.Contains(title)) return;
        ExitInAppFullscreen();
        var pip = new PipWindow(title.Title, title.Host is ReaderHost);
        title.Window = pip;
        pip.RestoreRequested += () => ShowTheater(title);
        pip.ClosedByUser += () =>
        {
            pip.Detach();
            title.Window = null;
            CloseTitle(title);
        };
        try
        {
            pip.EnterOverlay(_titles.Count(t => t.Window is not null) - 1);
            if (title.Host.Parent is Panel parent) parent.Children.Remove(title.Host);
            pip.Attach(title.Host);
            SetMini(title, true);
            pip.Activate();
            Nav.IsPaneOpen = _paneWasOpen;
        }
        catch (Exception ex)
        {
            Diag.Log($"pip: {ex.Message}");
            CloseWindow(title);
            // Keep the title in the open list if the OS cannot create a window.
            if (title.Host.Parent is Panel parent) parent.Children.Remove(title.Host);
        }
        SyncOverlayLayer();
    }

    private void ShowMini()
    {
        if (DockedTitle is { } title) MiniHost(title);
    }

    private static void CloseWindow(OpenTitle title)
    {
        var window = title.Window;
        title.Window = null;
        if (window is null) return;
        window.Detach();
        window.CloseQuiet();
    }

    private static void CloseTitle(OpenTitle title)
    {
        if (title.Host is PlayerHost player) player.Close();
        if (title.Host is ReaderHost reader) reader.Close();
    }

    private void RemoveTitle(OpenTitle title)
    {
        if (!_titles.Contains(title)) return;
        if (ReferenceEquals(title.Host.Parent, PlayerLayer))
        {
            ExitInAppFullscreen();
            Nav.IsPaneOpen = _paneWasOpen;
        }
        CloseWindow(title);
        if (title.Host.Parent is Panel parent) parent.Children.Remove(title.Host);
        _titles.Remove(title);
        if (title.Host is PlayerHost player)
            _pendingClose = _pendingClose.IsCompleted ? player.ReportsCompleted
                : Task.WhenAll(_pendingClose, player.ReportsCompleted);
        SyncOverlayLayer();
    }

    private void SyncOverlayLayer()
    {
        var active = PlayerLayer.Children.Count > 0;
        PlayerLayer.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        PlayerLayer.IsHitTestVisible = active;
        OpenTitlesButton.Visibility = _titles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenTitlesMenu.Items.Clear();
        foreach (var title in _titles)
        {
            var item = new MenuFlyoutSubItem { Text = title.Title };
            var show = new MenuFlyoutItem { Text = "Адкрыць" };
            show.Click += (_, _) => ShowTheater(title);
            var pip = new MenuFlyoutItem { Text = "Выплыўное акно" };
            pip.Click += (_, _) => { if (title.Window is { } window) window.Activate(); else MiniHost(title); };
            var close = new MenuFlyoutItem { Text = "Закрыць" };
            close.Click += (_, _) => CloseTitle(title);
            item.Items.Add(show);
            item.Items.Add(pip);
            item.Items.Add(close);
            OpenTitlesMenu.Items.Add(item);
        }
        SyncBack();
    }

    private void ToggleFullscreen(OpenTitle title)
    {
        if (_inAppFullscreen) { ExitInAppFullscreen(); return; }
        ShowTheater(title);
        EnterInAppFullscreen();
        if (title.Host is PlayerHost player) player.SetOsFullscreen(true);
        if (title.Host is ReaderHost reader) reader.SetOsFullscreen(true);
    }

    private void EnterInAppFullscreen()
    {
        _inAppFullscreen = true;
        Nav.Visibility = Visibility.Collapsed;
        PlayerLayer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 0, 0, 0));
        if (App.CurrentWindow is MainWindow window)
        {
            window.EnterVideoFullscreen();
        }
        (DockedTitle?.Host as Control)?.Focus(FocusState.Programmatic);
    }

    private void ExitInAppFullscreen()
    {
        if (!_inAppFullscreen)
        {
            return;
        }
        _inAppFullscreen = false;
        Nav.Visibility = Visibility.Visible;
        Nav.IsPaneVisible = true;
        PlayerLayer.Background = null;
        if (App.CurrentWindow is MainWindow window)
        {
            window.ExitVideoFullscreen();
        }
        if (DockedTitle?.Host is PlayerHost player) player.SetOsFullscreen(false);
        if (DockedTitle?.Host is ReaderHost reader) reader.SetOsFullscreen(false);
    }

    private void OnPageKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && _inAppFullscreen)
        {
            ExitInAppFullscreen();
            e.Handled = true;
        }
    }

    private void OnEscapeAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_inAppFullscreen)
        {
            ExitInAppFullscreen();
            args.Handled = true;
        }
    }

    private void RunGlobalSearch(string query)
    {
        ShowMini();
        _navTag = "search";
        ContentFrame.Navigate(typeof(SearchPage), new SearchArgs(query));
        SyncBack();
    }

    private void RunOpenMedia(string slug, string mediaType)
    {
        ShowMini();
        ContentFrame.Navigate(typeof(MediaDetailsPage), new MediaDetailsArgs(slug, mediaType));
        SyncBack();
    }

    private void RunNavigate(string tag)
    {
        ShowMini();
        SelectNavItem(tag);
        NavigateTo(tag);
    }

    private void SelectNavItem(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>()
                     .Concat(Nav.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            if (item.Tag as string == tag)
            {
                Nav.SelectedItem = item;
                break;
            }
        }
    }

    private void OnContentNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        => SyncBack();

    private void SyncBack() =>
        Nav.IsBackEnabled = _inAppFullscreen || DockedTitle is not null || ContentFrame.CanGoBack;

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (_inAppFullscreen) ExitInAppFullscreen();
        else if (DockedTitle is { } title) MiniHost(title);
        else if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        SyncBack();
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        ShowMini();
        if (args.InvokedItemContainer?.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private void NavigateTo(string tag)
    {
        if (CatalogTypes.Contains(tag))
        {
            if (_navTag == tag && ContentFrame.CurrentSourcePageType == typeof(CatalogPage))
            {
                return;
            }
            _navTag = tag;
            var title = tag switch
            {
                "anime" => Strings.Anime,
                "manga" => Strings.Manga,
                "cinema" => Strings.Cinema,
                "games" => Strings.Games,
                _ => Strings.Books,
            };
            ContentFrame.Navigate(typeof(CatalogPage), new CatalogArgs(tag, title));
            SyncBack();
            return;
        }

        if (Pages.TryGetValue(tag, out var pageType))
        {
            if (_navTag == tag && ContentFrame.CurrentSourcePageType == pageType)
            {
                return;
            }
            _navTag = tag;
            ContentFrame.Navigate(pageType);
            SyncBack();
        }
    }
}
