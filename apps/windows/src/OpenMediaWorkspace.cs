using Anibel.App.Playback;
using Anibel.App.Reading;
using Anibel.App.Services;
using Anibel.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App;

/// <summary>Owns open media views and their docked, fullscreen and PiP windows.</summary>
internal sealed class OpenMediaWorkspace(Panel PlayerLayer, DropDownButton OpenTitlesButton,
    MenuFlyout OpenTitlesMenu, NavigationView Nav, Control ContentFrame, Action changed)
{
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

    public async Task ClosePlaybackAsync()
    {
        _closing = true;
        foreach (var title in _titles.ToArray()) CloseTitle(title);
        await _pendingClose;
    }

    public async Task PlayEpisodeAsync(PlayerArgs args)
    {
        if (_closing) return;
        var title = FindTitle(args.MediaType, args.Slug)
            ?? AddTitle(args.MediaType, args.Slug, args.TitleLabel, new PlayerHost());
        ShowTheater(title);
        await ((PlayerHost)title.Host).PlayAsync(args);
    }

    public async Task ReadChapterAsync(ReaderArgs args)
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

    public void ShowMini()
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
            ContentFrame.Focus(FocusState.Programmatic);
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
        changed();
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

    public void ExitInAppFullscreen()
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

    public bool HasDockedMedia => DockedTitle is not null;
    public bool IsFullscreen => _inAppFullscreen;
    public bool HandleBack()
    {
        if (_inAppFullscreen) ExitInAppFullscreen();
        else if (DockedTitle is { } title) MiniHost(title);
        else return false;
        return true;
    }
}
