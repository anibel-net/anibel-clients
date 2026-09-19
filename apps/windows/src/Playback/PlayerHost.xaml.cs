using Anibel.App.Core;
using Anibel.App.Playback;
using Anibel.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Anibel.App.Playback;

public sealed partial class PlayerHost : UserControl
{
    private PlayerController? _controller;
    private readonly DispatcherQueueTimer _hideControls;
    private bool _updatingSlider;
    private bool _controlsHovered;
    private double _sliderDuration;
    private bool _mini;

    public event EventHandler? CollapseRequested;
    public event EventHandler? ExpandRequested;
    public event EventHandler? Closed;
    public event EventHandler? FullscreenRequested;
    public event EventHandler<bool>? IsPausedChanged;
    public event EventHandler<(double Pos, double Dur)>? ProgressChanged;

    public bool IsMini => _mini;
    public bool IsEmbedded => _controller?.Engine is WebView2Engine;
    public bool IsPaused => PlayPauseIcon.Symbol == Symbol.Play;
    public bool IsOpen => _controller is { IsDisposed: false } && Visibility == Visibility.Visible;
    private bool _externalChrome;
    private Views.PlayerArgs? _lastArgs;
    private Task? _opening;
    private PlayerSettingsMenu? _settingsMenu;
    public bool IsQualityMenuOpen => _settingsMenu?.IsOpen == true;
    public Task ShowQualityMenuAsync(FrameworkElement anchor)
    {
        _settingsMenu ??= new PlayerSettingsMenu(() => _controller, () => _lastArgs,
            changing => { QualityButton.IsEnabled = !changing; QualityLabel.Text = changing ? "Пераключэнне…" : "Налады"; },
            (message, error) => { DownloadNotice.Message = message; DownloadNotice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success; DownloadNotice.IsOpen = true; },
            message => SetStatus(message, error: true));
        return _settingsMenu.ShowAsync(anchor);
    }
    private async void OnQualityClick(object sender, RoutedEventArgs e) => await ShowQualityMenuAsync((FrameworkElement)sender);

    public PlayerHost()
    {
        InitializeComponent();
        SeekSlider.PointerExited += (_, _) => KeyboardNavigation.CloseToolTips(SeekSlider.XamlRoot);
        SeekSlider.Unloaded += (_, _) => KeyboardNavigation.CloseToolTips(SeekSlider.XamlRoot);
        _hideControls = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _hideControls.Interval = TimeSpan.FromSeconds(2.4);
        _hideControls.IsRepeating = false;
        _hideControls.Tick += (_, _) => HideControlsIfPlaying();
        Loaded += OnHostLoaded;
    }

    private void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        if (_externalChrome)
        {
            ApplyPipChrome();
        }
        if (_controller?.Engine is not { IsInitialized: true })
        {
            return;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (VideoPanel.ActualWidth > 0 && VideoPanel.ActualHeight > 0)
            {
                _controller?.Resize((uint)VideoPanel.ActualWidth, (uint)VideoPanel.ActualHeight);
            }
            _controller?.InvalidateSurface();
        });
    }

    public async Task PlayAsync(Views.PlayerArgs args)
    {
        Visibility = Visibility.Visible;
        _lastArgs = args;
        DownloadNotice.IsOpen = false;
        QualityLabel.Text = "Налады";
        EnglishTitleText.Text = args.EnglishTitle?.Trim() ?? "";
        EnglishTitleText.Visibility = string.IsNullOrWhiteSpace(EnglishTitleText.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        HideError();
        TitleText.Text = string.IsNullOrEmpty(args.TitleLabel) ? Strings.Episode : args.TitleLabel;
        if (!string.IsNullOrEmpty(args.EpisodeLabel))
        {
            TitleText.Text += Strings.EpisodeSuffix(args.EpisodeLabel);
        }
        EnsureController();
        SetBusy(true);
        var controller = _controller!;
        Task? opening = null;
        try
        {
            var surfaces = new PlayerSurfaces
            {
                VideoPanel = VideoPanel,
                SubtitleOverlay = SubtitleOverlay,
                EmbedHost = EmbedSurface,
            };
            SetStatus(Strings.ResolvingSource);
            opening = controller.StartAsync(args.Url, args.EpisodeId, surfaces, args.EpisodeType, args.DownloadId);
            _opening = opening;
            await opening;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!controller.IsDisposed && ReferenceEquals(_opening, opening))
            {
                SetStatus(ex.Message, error: true);
            }
        }
        finally
        {
            if (!controller.IsDisposed && ReferenceEquals(_opening, opening))
            {
                SetBusy(false);
            }
        }
    }

    public void SetMini(bool mini, bool externalChrome = false)
    {
        _settingsMenu?.Hide();
        _mini = mini;
        _externalChrome = externalChrome;
        var state = !mini ? "Theater" : externalChrome ? "Pip" : "Mini";
        VisualStateManager.GoToState(this, state, true);
        ApplyPipChrome();
        if (!mini)
        {
            ShowTheaterControls();
        }
        if (_controller?.Engine is { IsInitialized: true } && ActualWidth > 0 && ActualHeight > 0)
        {
            _controller.Resize((uint)Math.Max(1, VideoPanel.ActualWidth), (uint)Math.Max(1, VideoPanel.ActualHeight));
        }
    }

    private void ApplyPipChrome()
    {
        if (!_externalChrome)
        {
            TopBar.IsHitTestVisible = true;
            BottomBar.IsHitTestVisible = true;
            MiniHover.IsHitTestVisible = true;
            MiniHover.Visibility = Visibility.Collapsed;
            return;
        }
        KeyboardNavigation.CloseToolTips(XamlRoot);
        TopBar.Visibility = Visibility.Collapsed;
        BottomBar.Visibility = Visibility.Collapsed;
        MiniHover.Visibility = Visibility.Collapsed;
        TopBar.IsHitTestVisible = false;
        BottomBar.IsHitTestVisible = false;
        MiniHover.IsHitTestVisible = false;
        _hideControls.Stop();
    }

    public void TogglePlayback() => OnPlayPauseClick(this, new RoutedEventArgs());

    public void SeekTo(double seconds)
    {
        if (_controller?.Engine is { IsInitialized: true })
        {
            _controller.Seek(seconds);
        }
    }

    private Task _pendingClose = Task.CompletedTask;
    public Task ReportsCompleted => _pendingClose;
    public async Task CloseAsync() { Close(); await _pendingClose; }

    public void Close()
    {
        KeyboardNavigation.CloseToolTips(XamlRoot);
        _settingsMenu?.Hide();
        _controlsHovered = false;
        _hideControls.Stop();
        if (_controller is { } controller) { controller.Dispose(); _pendingClose = Task.WhenAll(_pendingClose, controller.ReportsCompleted); }
        _controller = null;
        HideError();
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureController()
    {
        if (_controller is { IsDisposed: false })
        {
            return;
        }
        var core = App.Services.GetRequiredService<ICoreClient>();
        _controller = new PlayerController(core);
        _controller.Ready += OnEngineReady;
        _controller.Ended += OnEngineEnded;
        _controller.PositionChanged += OnEnginePositionChanged;
        _controller.PauseChanged += OnEnginePauseChanged;
        _controller.BufferingChanged += buffering =>
        {
            StatusText.Text = buffering ? "Буферызацыя…" : "";
            SetBusy(buffering);
        };
        _controller.Error += message => SetStatus(message, error: true);
    }

    private void OnEngineReady()
    {
        // Layout may finish before the engine exists, so SizeChanged alone is insufficient.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (VideoPanel.ActualWidth > 0 && VideoPanel.ActualHeight > 0)
                _controller?.Resize((uint)VideoPanel.ActualWidth, (uint)VideoPanel.ActualHeight);
        });
        HideError();
        Overlay.Visibility = Visibility.Collapsed;
        var native = _controller?.Controls is not null;
        var transportVisibility = native ? Visibility.Visible : Visibility.Collapsed;
        foreach (var control in new FrameworkElement[] { PlayPauseButton, SeekBackButton, SeekForwardButton,
            SeekSlider, PositionText, MuteButton, VolumeSlider, QualityButton })
            control.Visibility = transportVisibility;
        TopBar.Padding = IsEmbedded ? new Thickness(16, 12, 16, 12) : new Thickness(16, 12, 16, 28);
        BottomBar.Padding = IsEmbedded ? new Thickness(12, 0, 12, 8) : new Thickness(16, 32, 16, 12);
        PlayPauseButton.IsEnabled =
            SeekBackButton.IsEnabled = SeekForwardButton.IsEnabled = native;
        SeekSlider.IsEnabled = native;
        OnEnginePauseChanged((_controller?.Engine as WindowsMediaEngine)?.IsPaused ?? false);
        if (!_mini)
        {
            ShowTheaterControls();
            if (!IsEmbedded) Focus(FocusState.Programmatic);
        }
        if (VolumeSlider.Value == 100 && _controller?.Controls is { Volume: > 0 } controls)
        {
            VolumeSlider.Value = controls.Volume;
        }
    }

    private void OnEngineEnded()
    {
        PlayPauseIcon.Symbol = Symbol.Play;
        MiniPlayIcon.Symbol = Symbol.Play;
        IsPausedChanged?.Invoke(this, true);
    }

    private void OnEnginePauseChanged(bool paused)
    {
        PlayPauseIcon.Symbol = paused ? Symbol.Play : Symbol.Pause;
        MiniPlayIcon.Symbol = paused ? Symbol.Play : Symbol.Pause;
        IsPausedChanged?.Invoke(this, paused);
    }

    private void OnEnginePositionChanged(double pos, double dur)
    {
        ProgressChanged?.Invoke(this, (pos, dur));
        PositionText.Text = $"{FormatTime(pos)} / {FormatTime(dur)}";
        _updatingSlider = true;
        try
        {
            if (dur > 0 && Math.Abs(_sliderDuration - dur) > 0.5)
            {
                _sliderDuration = dur;
                SeekSlider.Maximum = dur;
            }
            SeekSlider.Value = pos;
        }
        finally { _updatingSlider = false; }
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        try { _controller?.TogglePause(); }
        catch (Exception ex) { SetStatus(ex.Message, error: true); }
    }

    private void OnSeekBackClick(object sender, RoutedEventArgs e)
    {
        if (_controller?.Controls is { } controls)
        {
            _controller.Seek(Math.Max(0, controls.Position - 10));
        }
    }

    private void OnSeekForwardClick(object sender, RoutedEventArgs e)
    {
        if (_controller?.Controls is { } controls)
        {
            _controller.Seek(Math.Min(controls.Duration, controls.Position + 10));
        }
    }

    private void OnSeekValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || _controller?.Engine is not { IsInitialized: true } || !SeekSlider.IsEnabled || SeekSlider.Maximum <= 0)
        {
            return;
        }
        _controller.Seek(e.NewValue);
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        var engine = _controller?.Engine;
        if (engine is not { IsInitialized: true } || _controller?.Controls is not { } controls)
        {
            return;
        }
        _controller!.SetMute(!controls.IsMuted);
        VolumeIcon.Symbol = controls.IsMuted ? Symbol.Mute : Symbol.Volume;
    }

    private void OnVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_controller?.Engine is { IsInitialized: true } && _controller?.Controls is { } controls)
        {
            _controller!.SetVolume(e.NewValue);
            if (e.NewValue > 0 && controls.IsMuted)
            {
                _controller.SetMute(false);
                VolumeIcon.Symbol = Symbol.Volume;
            }
        }
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            _controller?.Resize((uint)e.NewSize.Width, (uint)e.NewSize.Height);
        }
    }

    private void OnSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_externalChrome || IsControlSource(e.OriginalSource))
        {
            return;
        }
        Focus(FocusState.Programmatic);
        OnPlayPauseClick(sender, e);
        ShowTheaterControls();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_externalChrome)
        {
            return;
        }
        if (_mini)
        {
            MiniHover.Visibility = Visibility.Visible;
        }
        else
        {
            ShowTheaterControls();
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_externalChrome || _mini)
        {
            return;
        }
        ShowTheaterControls();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_mini)
        {
            MiniHover.Visibility = Visibility.Collapsed;
        }
        else
        {
            _hideControls.Start();
        }
    }

    private void ShowTheaterControls()
    {
        if (_mini || _externalChrome)
        {
            return;
        }
        TopBar.Visibility = Visibility.Visible;
        BottomBar.Visibility = Visibility.Visible;
        _hideControls.Stop();
        _hideControls.Start();
    }

    private void OnControlsPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _controlsHovered = true;
        _hideControls.Stop();
    }

    private void OnControlsPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var bar = (FrameworkElement)sender;
        var point = e.GetCurrentPoint(bar).Position;
        // A child button can raise PointerExited while the pointer is still over the bar.
        _controlsHovered = point.X >= 0 && point.Y >= 0 && point.X < bar.ActualWidth && point.Y < bar.ActualHeight;
        if (!_controlsHovered) _hideControls.Start();
    }

    private void HideControlsIfPlaying()
    {
        // Embedded pages consume pointer input. Keep navigation outside the page visible.
        if (IsEmbedded || _controller?.Engine is not { IsInitialized: true }) return;
        if (IsQualityMenuOpen) { _hideControls.Start(); return; }
        if (_mini || _externalChrome || _controlsHovered)
        {
            return;
        }
        var paused = PlayPauseIcon.Symbol == Symbol.Play;
        if (paused)
        {
            return;
        }
        KeyboardNavigation.CloseToolTips(XamlRoot);
        TopBar.Visibility = Visibility.Collapsed;
        BottomBar.Visibility = Visibility.Collapsed;
    }

    private static bool IsControlSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button or Slider or WebView2)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void OnSurfaceDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_mini || IsControlSource(e.OriginalSource))
        {
            return;
        }
        e.Handled = true;
        FullscreenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSpaceAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (XamlRoot is null || KeyboardNavigation.IsEditing(XamlRoot)
            || (!_externalChrome && !KeyboardNavigation.FocusIsWithin(this))) return;
        args.Handled = true;
        OnPlayPauseClick(this, new RoutedEventArgs());
    }

    private void OnFullscreenAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (XamlRoot is null || KeyboardNavigation.IsEditing(XamlRoot)
            || (!_externalChrome && !KeyboardNavigation.FocusIsWithin(this))) return;
        args.Handled = true;
        FullscreenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMuteAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (XamlRoot is null || KeyboardNavigation.IsEditing(XamlRoot)
            || (!_externalChrome && !KeyboardNavigation.FocusIsWithin(this))) return;
        args.Handled = true;
        OnMuteClick(this, new RoutedEventArgs());
    }

    private void OnSeekBackAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (XamlRoot is null || KeyboardNavigation.IsEditing(XamlRoot)
            || (!_externalChrome && !KeyboardNavigation.FocusIsWithin(this))) return;
        args.Handled = true;
        OnSeekBackClick(this, new RoutedEventArgs());
    }

    private void OnSeekFwdAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (XamlRoot is null || KeyboardNavigation.IsEditing(XamlRoot)
            || (!_externalChrome && !KeyboardNavigation.FocusIsWithin(this))) return;
        args.Handled = true;
        OnSeekForwardClick(this, new RoutedEventArgs());
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke(this, EventArgs.Empty);
    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);
    private void OnFullscreenClick(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    public void SetOsFullscreen(bool fullscreen)
    {
        var icon = fullscreen ? Symbol.BackToWindow : Symbol.FullScreen;
        FullscreenIconBottom.Symbol = icon;
    }

    private static string FormatTime(double seconds) => PlaybackTimeConverter.Format(seconds);

    private void SetBusy(bool busy)
    {
        if (busy)
        {
            HideError();
        }
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Overlay.Visibility = busy ? Visibility.Visible : Overlay.Visibility;
    }

    private void SetStatus(string message, bool error = false)
    {
        if (error)
        {
            Overlay.Visibility = Visibility.Collapsed;
            BusyRing.IsActive = false;
            BusyRing.Visibility = Visibility.Collapsed;
            ErrorState.Message = message;
            ErrorHost.Visibility = Visibility.Visible;
            return;
        }
        HideError();
        StatusText.Text = message;
        StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(153, 255, 255, 255));
        Overlay.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorHost.Visibility = Visibility.Collapsed;
    }

    private async void OnRetryErrorClick(object sender, RoutedEventArgs e)
    {
        if (_lastArgs is not null)
        {
            await PlayAsync(_lastArgs);
        }
    }

    private void OnCopyErrorClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(ErrorState.Message);
}
