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
    private PipWindow? _pip;
    private bool _pipQuiet;

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

    public void Receive(GlobalSearchMessage message) => RunGlobalSearch(message.Query);
    public void Receive(OpenMediaMessage message) => RunOpenMedia(message.Slug, message.MediaType);
    public void Receive(NavigateMessage message) => RunNavigate(message.Tag);
    public void Receive(PlayEpisodeMessage message) => _ = PlayEpisodeAsync(message.Args);
    public void Receive(ReadChapterMessage message) => _ = ReadChapterAsync(message.Args);
    public void Receive(GoBackMessage message) => OnBackRequested(Nav, null!);

    private async Task PlayEpisodeAsync(PlayerArgs args)
    {
        MiniHost(ShellReader);
        ShowTheater(ShellPlayer);
        await ShellPlayer.PlayAsync(args);
    }

    private async Task ReadChapterAsync(ReaderArgs args)
    {
        MiniHost(ShellPlayer);
        ShowTheater(ShellReader);
        await ShellReader.OpenAsync(args);
    }

    private void ShowTheater(FrameworkElement host)
    {
        if (IsInPip(host))
        {
            DockFromPip(host);
            return;
        }
        if (!_inAppFullscreen)
        {
            _paneWasOpen = Nav.IsPaneOpen;
            Nav.IsPaneOpen = false;
        }
        EnsureInPlayerLayer(host);
        host.Visibility = Visibility.Visible;
        host.HorizontalAlignment = HorizontalAlignment.Stretch;
        host.VerticalAlignment = VerticalAlignment.Stretch;
        host.Width = double.NaN;
        host.Height = double.NaN;
        host.Margin = new Thickness(0);
        if (host == ShellPlayer) ShellPlayer.SetMini(false);
        if (host == ShellReader) ShellReader.SetMini(false);
        Canvas.SetZIndex(PlayerLayer, 2);
        Canvas.SetZIndex(host, 3);
        SyncOverlayLayer();
        SyncBack();
    }

    private void MiniHost(FrameworkElement host)
    {
        if (host.Visibility != Visibility.Visible)
        {
            return;
        }
        if (IsInPip(host))
        {
            return;
        }
        ExitInAppFullscreen();
        Nav.IsPaneOpen = _paneWasOpen;
        if (TryEnterPip(host))
        {
            return;
        }
        MiniHostInApp(host);
    }

    private void MiniHostInApp(FrameworkElement host)
    {
        EnsureInPlayerLayer(host);
        var isReader = host == ShellReader;
        host.HorizontalAlignment = HorizontalAlignment.Right;
        host.VerticalAlignment = VerticalAlignment.Bottom;
        host.Width = isReader ? 260 : 360;
        host.Height = isReader ? 340 : 220;
        host.Margin = new Thickness(16);
        if (host == ShellPlayer) ShellPlayer.SetMini(true);
        if (host == ShellReader) ShellReader.SetMini(true);
        Canvas.SetZIndex(PlayerLayer, 4);
        Canvas.SetZIndex(host, 4);
        SyncOverlayLayer();
        SyncBack();
    }

    private void ShowMini()
    {
        if (IsInPip(ShellPlayer) || IsInPip(ShellReader))
        {
            return;
        }
        if (ShellPlayer.Visibility == Visibility.Visible)
        {
            MiniHost(ShellPlayer);
            return;
        }
        MiniHost(ShellReader);
    }

    private bool IsInPip(FrameworkElement host) =>
        _pip is not null && ReferenceEquals(_pip.HostedContent, host);

    private bool TryEnterPip(FrameworkElement host)
    {
        try
        {
            _pip ??= CreatePipWindow();
            if (!_pip.EnterOverlay())
            {
                ClosePipQuiet();
                return false;
            }
            if (_pip.HostedContent is FrameworkElement other && !ReferenceEquals(other, host))
            {
                var displaced = _pip.Detach() as FrameworkElement;
                if (displaced is not null)
                {
                    MiniHostInApp(displaced);
                }
            }
            Reparent(host, _pip);
            if (host == ShellPlayer) ShellPlayer.SetMini(true, externalChrome: true);
            if (host == ShellReader) ShellReader.SetMini(true, externalChrome: true);
            host.DispatcherQueue.TryEnqueue(() =>
            {
                if (host == ShellPlayer) ShellPlayer.SetMini(true, externalChrome: true);
                if (host == ShellReader) ShellReader.SetMini(true, externalChrome: true);
            });
            _pip.Activate();
            SyncOverlayLayer();
            SyncBack();
            return true;
        }
        catch (Exception ex)
        {
            Diag.Log($"pip: {ex.Message}");
            ClosePipQuiet();
            return false;
        }
    }

    private PipWindow CreatePipWindow()
    {
        var pip = new PipWindow();
        pip.RestoreRequested += OnPipRestore;
        pip.ClosedByUser += OnPipClosedByUser;
        return pip;
    }

    private void OnPipRestore()
    {
        var host = _pip?.HostedContent as FrameworkElement;
        if (host is null)
        {
            ClosePipQuiet();
            return;
        }
        DockFromPip(host);
    }

    private void OnPipClosedByUser()
    {
        var host = _pip?.HostedContent as FrameworkElement;
        _pip = null;
        if (host is PlayerHost player)
        {
            EnsureInPlayerLayer(player);
            player.Close();
        }
        else if (host is ReaderHost reader)
        {
            EnsureInPlayerLayer(reader);
            reader.Close();
        }
        else if (host is not null)
        {
            EnsureInPlayerLayer(host);
        }
        SyncOverlayLayer();
        SyncBack();
    }

    private void DockFromPip(FrameworkElement host)
    {
        ClosePipQuiet();
        EnsureInPlayerLayer(host);
        ShowTheater(host);
    }

    private void ClosePipQuiet()
    {
        if (_pip is null)
        {
            return;
        }
        _pipQuiet = true;
        try
        {
            var leftover = _pip.Detach() as FrameworkElement;
            _pip.CloseQuiet();
            if (leftover is not null && leftover.Parent is null)
            {
                EnsureInPlayerLayer(leftover);
            }
        }
        finally
        {
            _pip = null;
            _pipQuiet = false;
        }
    }

    private void Reparent(FrameworkElement host, PipWindow pip)
    {
        if (host.Parent is Panel parent)
        {
            parent.Children.Remove(host);
        }
        pip.Attach(host);
    }

    private void EnsureInPlayerLayer(FrameworkElement host)
    {
        if (ReferenceEquals(host.Parent, PlayerLayer))
        {
            return;
        }
        if (host.Parent is Panel parent)
        {
            parent.Children.Remove(host);
        }
        if (!PlayerLayer.Children.Contains(host))
        {
            PlayerLayer.Children.Add(host);
        }
    }

    private void OnPlayerCollapse(object sender, EventArgs e) => MiniHost(ShellPlayer);
    private void OnPlayerExpand(object sender, EventArgs e)
    {
        MiniHost(ShellReader);
        ShowTheater(ShellPlayer);
    }

    private void OnPlayerClosed(object sender, EventArgs e)
    {
        if (!_pipQuiet && IsInPip(ShellPlayer))
        {
            ClosePipQuiet();
        }
        ExitInAppFullscreen();
        Nav.IsPaneOpen = _paneWasOpen;
        ShellPlayer.Visibility = Visibility.Collapsed;
        EnsureInPlayerLayer(ShellPlayer);
        SyncOverlayLayer();
        SyncBack();
    }

    private void OnReaderCollapse(object sender, EventArgs e) => MiniHost(ShellReader);
    private void OnReaderExpand(object sender, EventArgs e)
    {
        MiniHost(ShellPlayer);
        ShowTheater(ShellReader);
    }

    private void OnReaderClosed(object sender, EventArgs e)
    {
        if (!_pipQuiet && IsInPip(ShellReader))
        {
            ClosePipQuiet();
        }
        ExitInAppFullscreen();
        Nav.IsPaneOpen = _paneWasOpen;
        ShellReader.Visibility = Visibility.Collapsed;
        EnsureInPlayerLayer(ShellReader);
        SyncOverlayLayer();
        SyncBack();
    }

    private void SyncOverlayLayer()
    {
        var inPip = _pip is not null;
        var active = !inPip && (ShellPlayer.Visibility == Visibility.Visible
            || ShellReader.Visibility == Visibility.Visible);
        PlayerLayer.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        PlayerLayer.IsHitTestVisible = active;
    }

    private void OnPlayerFullscreen(object sender, EventArgs e)
    {
        if (_inAppFullscreen)
        {
            ExitInAppFullscreen();
            return;
        }
        MiniHost(ShellReader);
        ShowTheater(ShellPlayer);
        EnterInAppFullscreen();
        ShellPlayer.SetOsFullscreen(true);
    }

    private void OnReaderFullscreen(object sender, EventArgs e)
    {
        if (_inAppFullscreen)
        {
            ExitInAppFullscreen();
            return;
        }
        MiniHost(ShellPlayer);
        ShowTheater(ShellReader);
        EnterInAppFullscreen();
        ShellReader.SetOsFullscreen(true);
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
        ShellPlayer.Focus(FocusState.Programmatic);
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
        ShellPlayer.SetOsFullscreen(false);
        ShellReader.SetOsFullscreen(false);
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

    private void SyncBack()
    {
        var overlayOpen =
            _inAppFullscreen
            || (ShellPlayer.Visibility == Visibility.Visible && !ShellPlayer.IsMini)
            || (ShellReader.Visibility == Visibility.Visible && !ShellReader.IsMini);
        var enabled = overlayOpen || ContentFrame.CanGoBack || ContentFrame.BackStackDepth > 0;
        Nav.IsBackEnabled = enabled;
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ShellPlayer.Visibility == Visibility.Visible && !ShellPlayer.IsMini)
        {
            if (_inAppFullscreen)
            {
                ExitInAppFullscreen();
                SyncBack();
                return;
            }
            MiniHost(ShellPlayer);
            SyncBack();
            return;
        }
        if (ShellReader.Visibility == Visibility.Visible && !ShellReader.IsMini)
        {
            if (_inAppFullscreen)
            {
                ExitInAppFullscreen();
                SyncBack();
                return;
            }
            MiniHost(ShellReader);
            SyncBack();
            return;
        }
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
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
