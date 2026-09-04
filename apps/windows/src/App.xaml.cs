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
        var builder = Host.CreateApplicationBuilder();
        var settings = new SettingsService();
        settings.Load(); // persisted language/API endpoints — before anything reads them
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<ApiCache>();
        builder.Services.AddSingleton<CoreClient>();
        builder.Services.AddSingleton<ICoreClient>(sp => sp.GetRequiredService<CoreClient>());
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
        await CheckCoreAsync();
        if (!Services.GetRequiredService<CoreClient>().IsConnected)
        {
            return;
        }
        StartEventPump();
        RestoreSession();
    }

    /// <summary>Push the DPAPI-protected token (if any) into the core session.</summary>
    private async void RestoreSession()
    {
        var session = Services.GetRequiredService<SessionService>();
        if (session.Token is { Length: > 0 } token)
        {
            try
            {
                await Services.GetRequiredService<CoreClient>().SetTokenAsync(token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[session] restore failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Startup health + version check of anibel_core.dll (arch §5.2): surface
    /// a dialog instead of failing later on every op.
    /// </summary>
    private async Task CheckCoreAsync()
    {
        try
        {
            var core = Services.GetRequiredService<CoreClient>();
            var version = await core.GetVersionAsync();
            Diag.Log($"core ok: {version.Name} {version.Version}");
        }
        catch (Exception ex)
        {
            Diag.Log($"core check FAILED: {ex.Message}");
            await ShowDialogAsync(
                Strings.CoreErrorTitle,
                Strings.CoreInitError(ex.Message));
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
    /// Polls the core event queue on the UI thread (auth expiry, …). ~60ms is
    /// enough for our event granularity.
    /// </summary>
    private void StartEventPump()
    {
        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(60);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            var core = Services.GetRequiredService<CoreClient>();
            foreach (var evt in core.DrainEvents())
            {
                if (evt.TryGetProperty("e", out var kind))
                {
                    var name = kind.GetString();
                    System.Diagnostics.Debug.WriteLine($"[core>] {name}");
                    if (name == "auth.expired")
                    {
                        Diag.Log("core: auth.expired — clearing local session");
                        Services.GetRequiredService<SessionService>().Clear();
                        WeakReferenceMessenger.Default.Send(new SessionExpiredMessage());
                        WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
                    }
                }
            }
        };
        timer.Start();
    }
}
