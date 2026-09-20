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
    private readonly OpenMediaWorkspace _media;

    public MainPage()
    {
        InitializeComponent();
        _media = new OpenMediaWorkspace(PlayerLayer, OpenTitlesButton, OpenTitlesMenu, Nav, ContentFrame, SyncBack);
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

    internal bool HasOpenMedia => _media.HasOpenMedia;
    internal bool HasDockedMedia => _media.HasDockedMedia;
    internal Control ContentFocusTarget => ContentFrame;

    internal void FocusCards()
    {
        if (ContentFrame.Content is DependencyObject content)
            KeyboardNavigation.Find<MediaCollectionView>(content)?.FocusFirstCard();
    }

    public Task ClosePlaybackAsync() => _media.ClosePlaybackAsync();

    public void Receive(GlobalSearchMessage message) => RunGlobalSearch(message.Query);
    public void Receive(OpenProfileMessage message)
    {
        _media.ShowMini();
        _navTag = null;
        ContentFrame.Navigate(typeof(ProfilePage), new ProfileArgs(message.Username));
        SyncBack();
    }
    public void Receive(OpenMediaMessage message) => RunOpenMedia(message.Slug, message.MediaType);
    public void Receive(NavigateMessage message) => RunNavigate(message.Tag);
    public void Receive(PlayEpisodeMessage message) => _ = _media.PlayEpisodeAsync(message.Args);
    public void Receive(ReadChapterMessage message) => _ = _media.ReadChapterAsync(message.Args);
    public void Receive(GoBackMessage message) => OnBackRequested(Nav, null!);

    internal bool HandleMouseNavigation(Microsoft.UI.Input.PointerUpdateKind kind)
    {
        switch (kind)
        {
            case Microsoft.UI.Input.PointerUpdateKind.XButton1Released:
                OnBackRequested(Nav, null!);
                return true;
            case Microsoft.UI.Input.PointerUpdateKind.XButton2Released:
                if (ContentFrame.CanGoForward)
                {
                    _media.ShowMini();
                    ContentFrame.GoForward();
                    SyncBack();
                }
                return true;
            default: return false;
        }
    }

    private void OnPageKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && _media.IsFullscreen)
        {
            _media.ExitInAppFullscreen();
            e.Handled = true;
        }
    }

    private void OnEscapeAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_media.IsFullscreen)
        {
            _media.ExitInAppFullscreen();
            args.Handled = true;
        }
    }

    private void RunGlobalSearch(string query)
    {
        _media.ShowMini();
        _navTag = "search";
        ContentFrame.Navigate(typeof(SearchPage), new SearchArgs(query));
        SyncBack();
    }

    private void RunOpenMedia(string slug, string mediaType)
    {
        _media.ShowMini();
        ContentFrame.Navigate(typeof(MediaDetailsPage), new MediaDetailsArgs(slug, mediaType));
        SyncBack();
    }

    private void RunNavigate(string tag)
    {
        _media.ShowMini();
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
    {
        // This target remains loaded when a page or its loading controls disappear.
        ContentFrame.Focus(FocusState.Programmatic);
        SyncBack();
    }

    private void SyncBack() =>
        Nav.IsBackEnabled = _media.IsFullscreen || _media.HasDockedMedia || ContentFrame.CanGoBack;

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (!_media.HandleBack() && ContentFrame.CanGoBack) ContentFrame.GoBack();
        SyncBack();
    }

    private async void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag as string == "shortcuts")
        {
            if (App.CurrentWindow is MainWindow window) await window.ShowShortcutHelpAsync();
            return;
        }
        _media.ShowMini();
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
