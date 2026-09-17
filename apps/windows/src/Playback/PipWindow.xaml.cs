using System.Runtime.InteropServices;
using Anibel.App.Reading;
using Anibel.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Anibel.App.Playback;

/// <summary>
/// One title per window. Video uses 16:9; manga uses a freely resizable reading window.
/// </summary>
public sealed partial class PipWindow : Window
{
    private readonly bool _reader;

    private bool _quiet;
    private bool _controlsHovered;
    private bool _sizing;
    private bool _updatingSeek;
    private bool _dragging;
    private readonly DispatcherQueueTimer _hideChrome;
    private PointInt32 _dragOrigin;
    private Native.Point _dragCursor;
    private UIElement? _content;
    private PlayerHost? _player;

    public event Action? RestoreRequested;
    public event Action? ClosedByUser;

    public UIElement? HostedContent => _content;

    public PipWindow(string title, bool reader)
    {
        InitializeComponent();
        Title = title;
        _reader = reader;
        WindowTitle.Text = title;
        ReaderDragHandle.Visibility = reader ? Visibility.Visible : Visibility.Collapsed;
        Shade.Visibility = reader ? Visibility.Collapsed : Visibility.Visible;
        Chrome.Background = reader ? null : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }
        Closed += OnClosed;
        AppWindow.Changed += OnAppWindowChanged;
        _hideChrome = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _hideChrome.Interval = TimeSpan.FromMilliseconds(1400);
        _hideChrome.IsRepeating = false;
        _hideChrome.Tick += (_, _) => HideChrome();
        HideChrome();
    }

    public void EnterOverlay(int slot)
    {
        try
        {
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            AppWindow.SetPresenter(presenter);
        }
        catch (Exception ex)
        {
            Diag.Log($"pip presenter: {ex.Message}");
        }

        var size = DefaultSize();
        AppWindow.Resize(size);
        PlaceBottomRight(slot);
        HideChrome();
    }

    public void Attach(UIElement content)
    {
        if (ReferenceEquals(_content, content))
        {
            return;
        }
        DetachPlayer();
        if (_content is not null)
        {
            Host.Children.Remove(_content);
        }
        _content = content;
        if (content is FrameworkElement fe)
        {
            fe.Width = double.NaN;
            fe.Height = double.NaN;
            fe.Margin = new Thickness(0);
            fe.HorizontalAlignment = HorizontalAlignment.Stretch;
            fe.VerticalAlignment = VerticalAlignment.Stretch;
            fe.Visibility = Visibility.Visible;
        }
        Host.Children.Add(content);
        if (content is PlayerHost player)
        {
            BindPlayer(player);
        }
        else
        {
            PlayButton.Visibility = Visibility.Collapsed;
            SeekSlider.Visibility = Visibility.Collapsed;
        }
    }

    public UIElement? Detach()
    {
        DetachPlayer();
        var content = _content;
        if (content is not null)
        {
            Host.Children.Remove(content);
        }
        _content = null;
        return content;
    }

    public void CloseQuiet()
    {
        _quiet = true;
        Close();
    }

    private void BindPlayer(PlayerHost player)
    {
        _player = player;
        UpdatePlayerChrome();
        PlayIcon.Symbol = player.IsPaused ? Symbol.Play : Symbol.Pause;
        player.IsPausedChanged += OnPlayerPaused;
        player.ProgressChanged += OnPlayerProgress;
    }

    private void DetachPlayer()
    {
        QualityButton.Visibility = Visibility.Collapsed;
        if (_player is null)
        {
            return;
        }
        _player.IsPausedChanged -= OnPlayerPaused;
        _player.ProgressChanged -= OnPlayerProgress;
        _player = null;
    }

    private void OnPlayerPaused(object? sender, bool paused)
    {
        PlayIcon.Symbol = paused ? Symbol.Play : Symbol.Pause;
        UpdatePlayerChrome();
    }

    private void UpdatePlayerChrome()
    {
        var embedded = _player?.IsEmbedded == true;
        QualityButton.Visibility = PlayButton.Visibility = SeekSlider.Visibility =
            embedded ? Visibility.Collapsed : Visibility.Visible;
        // Reserve space for restore/close, outside the embedded browser's input area.
        Host.Margin = embedded ? new Thickness(0, 48, 0, 0) : new Thickness(0);
        Shade.Visibility = embedded ? Visibility.Collapsed : Visibility.Visible;
        if (embedded) ShowChrome();
    }

    private void OnPlayerProgress(object? sender, (double Pos, double Dur) e)
    {
        if (e.Dur > 0 && Math.Abs(SeekSlider.Maximum - e.Dur) > 0.5)
        {
            SeekSlider.Maximum = e.Dur;
        }
        if (!_updatingSeek)
        {
            _updatingSeek = true;
            SeekSlider.Value = Math.Max(0, e.Pos);
            _updatingSeek = false;
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
        => _player?.TogglePlayback();

    private async void OnQualityClick(object sender, RoutedEventArgs e)
    {
        if (_player is { } player) await player.ShowQualityMenuAsync((FrameworkElement)sender);
    }

    private void OnSeekChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSeek)
        {
            return;
        }
        _player?.SeekTo(e.NewValue);
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
        => RestoreRequested?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => Close();

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
        => ShowChrome();

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
        => ShowChrome();

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < Root.ActualWidth && point.Y < Root.ActualHeight) return;
        _hideChrome.Stop();
        HideChrome();
    }

    private void ShowChrome()
    {
        Chrome.Opacity = 1;
        if (_content is ReaderHost reader) reader.SetOverlayVisible(true);
        Chrome.IsHitTestVisible = true;
        _hideChrome.Stop();
        _hideChrome.Start();
    }

    private void HideChrome()
    {
        if (_player?.IsEmbedded == true) return;
        if (_dragging || _controlsHovered || _player?.IsQualityMenuOpen == true || _content is ReaderHost { KeepOverlayVisible: true }
            || (Root.XamlRoot is not null && FocusManager.GetFocusedElement(Root.XamlRoot) is Control { FocusState: FocusState.Keyboard } && KeyboardNavigation.FocusIsWithin(Chrome)))
        {
            _hideChrome.Start();
            return;
        }
        Chrome.Opacity = 0;
        if (_content is ReaderHost reader) reader.SetOverlayVisible(false);
        Chrome.IsHitTestVisible = false;
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if ((_reader && !IsWithin(e.OriginalSource, ReaderDragHandle))
            || (_player?.IsEmbedded == true && IsWithin(e.OriginalSource, Host))
            || IsControlSource(e.OriginalSource) || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (!Native.GetCursorPos(out _dragCursor))
        {
            return;
        }
        _dragOrigin = AppWindow.Position;
        _dragging = true;
        Root.CapturePointer(e.Pointer);
        Root.PointerMoved += OnDragMoved;
        Root.PointerReleased += OnDragReleased;
        Root.PointerCaptureLost += OnDragReleased;
        e.Handled = true;
    }

    private void OnDragMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || !Native.GetCursorPos(out var now))
        {
            return;
        }
        AppWindow.Move(new PointInt32(
            (int)Math.Clamp((long)_dragOrigin.X + now.X - _dragCursor.X, int.MinValue, int.MaxValue),
            (int)Math.Clamp((long)_dragOrigin.Y + now.Y - _dragCursor.Y, int.MinValue, int.MaxValue)));
    }

    private void OnDragReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Root.PointerMoved -= OnDragMoved;
        Root.PointerReleased -= OnDragReleased;
        Root.PointerCaptureLost -= OnDragReleased;
        try
        {
            Root.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
        }
    }

    private void OnControlsEntered(object sender, PointerRoutedEventArgs e) => _controlsHovered = true;
    private void OnControlsExited(object sender, PointerRoutedEventArgs e) { _controlsHovered = false; ShowChrome(); }
    private void OnRootGotFocus(object sender, RoutedEventArgs e)
    {
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is Control { FocusState: FocusState.Keyboard }) ShowChrome();
    }
    private static bool IsWithin(object source, DependencyObject ancestor)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static bool IsControlSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase or Slider or ComboBox)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_quiet || _sizing || !args.DidSizeChange)
        {
            return;
        }
        var max = MaxSize();
        var size = _reader
            ? PipSize.Reader(sender.Size.Width, sender.Size.Height, max.Width, max.Height, Root.XamlRoot?.RasterizationScale ?? 1)
            : PipSize.Video(sender.Size.Width, max.Width, max.Height);
        if (Math.Abs(sender.Size.Width - size.Width) > 2 || Math.Abs(sender.Size.Height - size.Height) > 2)
        {
            _sizing = true;
            try { sender.Resize(new SizeInt32(size.Width, size.Height)); }
            finally { _sizing = false; }
        }
    }

    private SizeInt32 DefaultSize()
    {
        var max = MaxSize();
        if (_reader) return new SizeInt32(Math.Min(520, max.Width), Math.Min(780, max.Height));
        var size = PipSize.Video(Math.Min(960, (int)(max.Width * 0.42)), max.Width, max.Height);
        return new SizeInt32(size.Width, size.Height);
    }

    private SizeInt32 MaxSize()
    {
        try
        {
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var w = Math.Max(1, work.Width - 32);
            var h = Math.Max(1, work.Height - 32);
            return new SizeInt32(w, h);
        }
        catch
        {
            return new SizeInt32(1920, 1080);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _hideChrome.Stop();
        AppWindow.Changed -= OnAppWindowChanged;
        DetachPlayer();
        if (!_quiet)
        {
            ClosedByUser?.Invoke();
        }
    }

    private void PlaceBottomRight(int slot)
    {
        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            var work = display.WorkArea;
            var size = AppWindow.Size;
            var offset = Math.Clamp(slot, 0, 7) * 32;
            var x = work.X + work.Width - size.Width - 24 - offset;
            var y = work.Y + work.Height - size.Height - 48 - offset;
            AppWindow.Move(new PointInt32(Math.Max(work.X, x), Math.Max(work.Y, y)));
        }
        catch (Exception ex)
        {
            Diag.Log($"pip place: {ex.Message}");
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point lpPoint);
    }
}
