using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App;

/// <summary>
/// Application bootstrap. DI via Microsoft.Extensions.Hosting; the Rust core
/// (anibel_core.dll) is hosted through <see cref="Core"/>.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static Window? CurrentWindow { get; private set; }

    private Window? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            Diag.Log($"UNHANDLED XAML: {e.Exception}");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Diag.Log($"UNHANDLED APPDOMAIN: {e.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diag.Log($"UNOBSERVED TASK: {e.Exception}");
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if PLAYBACK_SMOKE
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length == 3 && commandLine[1] == "--native-playback-smoke")
        {
            new global::PlaybackSmoke.NativePlaybackSmoke(this, System.IO.Path.GetFullPath(commandLine[2])).Start();
            return;
        }
#endif
        var builder = Host.CreateApplicationBuilder();
        var settings = new SettingsService();
        settings.Load(); // persisted language/API endpoints — before anything reads them
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<CoreClient>();
        builder.Services.AddSingleton<ICoreClient>(sp => sp.GetRequiredService<CoreClient>());
        builder.Services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        builder.Services.AddSingleton<SessionService>();
        builder.Services.AddSingleton<SearchHistory>();
        builder.Services.AddSingleton<DownloadService>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<CatalogViewModel>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<MediaDetailsViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<ProfileListViewModel>();
        builder.Services.AddSingleton<DownloadsViewModel>();
        Services = builder.Build().Services;

        Ui.Load(settings);

        _window = new MainWindow();
        CurrentWindow = _window;
        _window.Activate();
        _ = Services.GetRequiredService<DownloadService>();

        _ = BootCoreAsync();
    }

    /// <summary>
    /// Init the native DLL (lazy CoreClient) before the event pump or
    /// session restore touch it, so a missing DLL surfaces as a dialog
    /// instead of DllNotFoundException during construction.
    /// </summary>
    private async Task BootCoreAsync()
    {
        if (!await CheckCoreAsync())
        {
            return;
        }
        try
        {
            await Services.GetRequiredService<SessionService>().RestoreAsync();
            WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
            await Services.GetRequiredService<SearchHistory>().RefreshAsync();
        }
        catch (Exception ex) { await ShowDialogAsync(Strings.CoreErrorTitle, Ui.DisplayMessage(ex)); }
        StartEventPump();
    }

    /// <summary>
    /// Startup health + version check of anibel_core.dll (arch §5.2): surface
    /// a dialog instead of failing later on every op.
    /// </summary>
    private async Task<bool> CheckCoreAsync()
    {
        try
        {
            var core = Services.GetRequiredService<CoreClient>();
            var version = await core.GetVersionAsync();
            var capabilities = await core.CallAsync<System.Text.Json.JsonElement>("capabilities");
            if (capabilities.GetProperty("protocolVersion").GetInt32() != 2) throw new InvalidOperationException("Unsupported core protocol. Rebuild the app and core together.");
            Diag.Log($"core ok: {version.Name} {version.Version}");
            return true;
        }
        catch (Exception ex)
        {
            Diag.Log($"core check FAILED: {ex.Message}");
            await ShowDialogAsync(
                Strings.CoreErrorTitle,
                Strings.CoreInitError(ex.Message));
            return false;
        }
    }

    private async Task ShowDialogAsync(string title, string message)
    {
        try
        {
            if (_window?.Content?.XamlRoot is not { } root)
            {
                return;
            }
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = Strings.Ok,
                XamlRoot = root,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Diag.Log($"dialog failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads core snapshots on the UI thread. Events are optional notifications.
    /// </summary>
    private void StartEventPump()
    {
        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.IsRepeating = true;
        var busy = false;
        timer.Tick += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            try
            {
                var core = Services.GetRequiredService<CoreClient>();
                _ = core.DrainEvents();
                var session = Services.GetRequiredService<SessionService>();
                var revision = session.Revision;
                await session.RefreshAsync();
                if (revision != session.Revision) WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
                await Services.GetRequiredService<DownloadService>().RefreshAsync();
            }
            catch (Exception ex) { Diag.Log($"core state update: {ex.Message}"); }
            finally { busy = false; }
        };
        timer.Start();
    }
}
