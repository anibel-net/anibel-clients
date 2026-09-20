using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App.Views;

public sealed partial class SettingsPage : Page
{
    private static readonly string[] LangKeys = ["be", "ru"];

    private readonly SettingsService _settings;
    private bool _initialized;
    public AppUpdateService Updates { get; }

    public SettingsPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Updates = App.Services.GetRequiredService<AppUpdateService>();
        InitializeComponent();
        _settings = App.Services.GetRequiredService<SettingsService>();

        // populate BEFORE attaching the handler so restoring state never saves
        LanguageCombo.SelectedIndex = Math.Max(0, Array.IndexOf(LangKeys, _settings.Language));
        BackgroundCombo.SelectedIndex = (int)_settings.Background;
        ApiUrlBox.Text = _settings.ApiBaseUrl;
        VideoApiUrlBox.Text = _settings.VideoBaseUrl;
        _initialized = true;
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e) => await Updates.CheckAsync();
    private async void OnRestartUpdateClick(object sender, RoutedEventArgs e)
    {
        if (App.CurrentWindow is MainWindow window)
        {
            try { await window.RestartForUpdateAsync(); }
            catch (Exception ex) { ShowStatus(ex.Message); }
        }
    }
    private async void OnReleasesClick(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(ReleaseUpdateSource.Repository + "/releases", UriKind.Absolute, out var uri))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }
        _settings.Language = LangKeys[Math.Max(0, LanguageCombo.SelectedIndex)];
        _settings.Save();
        ShowStatus(Strings.LanguageRestartNote);
    }

    private void OnBackgroundChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || BackgroundCombo.SelectedIndex < 0) return;
        _settings.Background = (WindowBackground)BackgroundCombo.SelectedIndex;
        _settings.Save();
        if (App.CurrentWindow is MainWindow window) window.ApplyBackground();
    }

    private void OnSaveUrlsClick(object sender, RoutedEventArgs e)
    {
        _settings.ApiBaseUrl = string.IsNullOrWhiteSpace(ApiUrlBox.Text)
            ? "https://anibel.net/graphql"
            : ApiUrlBox.Text.Trim();
        _settings.VideoBaseUrl = string.IsNullOrWhiteSpace(VideoApiUrlBox.Text)
            ? "https://api.anibel.stream"
            : VideoApiUrlBox.Text.Trim();
        _settings.Save();
        ShowStatus(Strings.UrlsSaved);
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Services.GetRequiredService<ICoreClient>().CallAsync<System.Text.Json.JsonElement>(CoreCommand.ClearPlaybackAssets);
            ShowStatus(Strings.CacheCleared);
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.Error(ex.Message));
        }
        await Task.CompletedTask;
    }

    private async void OnClearApiCacheClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Services.GetRequiredService<CoreClient>().ClearCacheAsync();
            ShowStatus(Strings.CatalogCacheCleared);
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.Error(ex.Message));
        }
    }

    private void ShowStatus(string message)
    {
        Status.Text = message;
        Status.Visibility = Visibility.Visible;
    }
}
