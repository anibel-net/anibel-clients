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
    private readonly DispatcherQueueTimer _seekDebounce;
    private readonly DispatcherQueueTimer _hideControls;
    private bool _updatingSlider;
    private bool _userSeeking;
    private double _sliderDuration;
    private bool _mini;

    public event EventHandler? CollapseRequested;
    public event EventHandler? ExpandRequested;
    public event EventHandler? Closed;
    public event EventHandler? FullscreenRequested;
    public event EventHandler<bool>? IsPausedChanged;
    public event EventHandler<(double Pos, double Dur)>? ProgressChanged;

    public bool IsMini => _mini;
    public bool IsPaused => PlayPauseIcon.Symbol == Symbol.Play;
    public bool IsOpen => _controller is { IsDisposed: false } && Visibility == Visibility.Visible;
    private bool _externalChrome;
    private Views.PlayerArgs? _lastArgs;
    private Task? _opening;
    private MenuFlyout? _qualityMenu;
    public bool IsQualityMenuOpen => _qualityMenu?.IsOpen == true;

    public async Task ShowQualityMenuAsync(FrameworkElement anchor)
    {
        var controller = _controller;
        var engine = controller?.Engine as MpvEngine;
        _qualityMenu?.Hide();
        var menu = _qualityMenu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Загрузка…", IsEnabled = false });
        menu.ShowAt(anchor);
        try
        {
            if (engine is null)
            {
                menu.Items.Clear();
                menu.Items.Add(new MenuFlyoutItem { Text = "Якасць задаецца ўбудаваным плэерам", IsEnabled = false });
                return;
            }
            var core = App.Services.GetRequiredService<ICoreClient>();
            var qualities = await core.CallAsync<VideoQualitiesDto>("videoQualities", new { tracks = engine.ReadVideoTracks() });
            if (!ReferenceEquals(controller, _controller) || !ReferenceEquals(engine, controller?.Engine)) { menu.Hide(); return; }
            menu.Items.Clear();
            foreach (var quality in qualities.Choices)
            {
                var item = new ToggleMenuFlyoutItem { Text = quality.Label, IsChecked = quality.Selected };
                item.Click += async (_, _) =>
                {
                    menu.Hide();
                    try
                    {
                        if (!ReferenceEquals(engine, _controller?.Engine)) return;
                        var selection = await core.CallAsync<VideoQualitiesDto>("videoQualities", new { tracks = engine.ReadVideoTracks(), select = quality.Id });
                        if (ReferenceEquals(engine, _controller?.Engine) && selection.Video is { } id) engine.SetVideoTrack(id);
                    }
                    catch (Exception ex)
                    {
                        if (!ReferenceEquals(_qualityMenu, menu) || anchor.XamlRoot is null) return;
                        menu.Items.Clear();
                        menu.Items.Add(new MenuFlyoutItem { Text = Ui.DisplayMessage(ex), IsEnabled = false });
                        menu.ShowAt(anchor);
                    }
                };
                menu.Items.Add(item);
            }
            if (qualities.Choices.Length <= 1)
            {
                if (qualities.Choices.Length > 0) menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(new MenuFlyoutItem { Text = "Іншыя варыянты якасці недаступныя", IsEnabled = false });
            }
        }
        catch (Exception ex)
        {
            menu.Items.Clear();
            menu.Items.Add(new MenuFlyoutItem { Text = Ui.DisplayMessage(ex), IsEnabled = false });
        }
    }

    private async void OnQualityClick(object sender, RoutedEventArgs e) => await ShowQualityMenuAsync((FrameworkElement)sender);

    public PlayerHost()
    {
        InitializeComponent();
        _seekDebounce = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _seekDebounce.Interval = TimeSpan.FromMilliseconds(150);
        _seekDebounce.IsRepeating = false;
        _seekDebounce.Tick += (_, _) =>
        {
            if (_controller?.Engine is { IsInitialized: true })
            {
                _controller.Seek(SeekSlider.Value);
            }
            _userSeeking = false;
        };
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
                EmbedHost = ContentRoot,
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
        _qualityMenu?.Hide();
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
        _qualityMenu?.Hide();
        _seekDebounce.Stop();
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
        _controller.Error += message => SetStatus(message, error: true);
    }

    private void OnEngineReady()
    {
        HideError();
        Overlay.Visibility = Visibility.Collapsed;
        PlayPauseButton.IsEnabled =
            SeekBackButton.IsEnabled = SeekForwardButton.IsEnabled = true;
        SeekSlider.IsEnabled = true;
        OnEnginePauseChanged((_controller?.Engine as MpvEngine)?.IsPaused ?? false);
        if (!_mini)
        {
            ShowTheaterControls();
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
        if (dur > 0 && Math.Abs(_sliderDuration - dur) > 0.5)
        {
            _sliderDuration = dur;
            SeekSlider.Maximum = dur;
        }
        if (!_userSeeking)
        {
            _updatingSlider = true;
            SeekSlider.Value = pos;
            _updatingSlider = false;
        }
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
        if (_updatingSlider || _controller?.Engine is not { IsInitialized: true } || !_seekDebounceCanSeek())
        {
            return;
        }
        _userSeeking = true;
        _seekDebounce.Start();
    }

    private bool _seekDebounceCanSeek() => SeekSlider.IsEnabled && SeekSlider.Maximum > 0;

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
        Focus(FocusState.Programmatic);
    }

    private void HideControlsIfPlaying()
    {
        if (IsQualityMenuOpen) { _hideControls.Start(); return; }
        if (_mini || _externalChrome || _userSeeking)
        {
            return;
        }
        var paused = PlayPauseIcon.Symbol == Symbol.Play;
        if (paused)
        {
            return;
        }
        TopBar.Visibility = Visibility.Collapsed;
        BottomBar.Visibility = Visibility.Collapsed;
    }

    private static bool IsControlSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button or Slider)
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
        FullscreenIcon.Symbol = icon;
        FullscreenIconBottom.Symbol = icon;
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds)) return "0:00";
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

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
