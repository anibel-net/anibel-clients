using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.IO;
using System.Reflection;

namespace Anibel.App.Views;

public sealed partial class SettingsPage : Page
{
    private static readonly string[] LangKeys = ["be", "ru", "en"];
    private static readonly string[] LangNames = ["Беларуская", "Русский", "English"];

    private readonly CoreClient _core;
    private readonly SettingsService _settings;
    private bool _initialized;

    public SettingsPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
        _core = App.Services.GetRequiredService<CoreClient>();
        _settings = App.Services.GetRequiredService<SettingsService>();

        // populate BEFORE attaching the handler so restoring state never saves
        LanguageCombo.SelectedIndex = Math.Max(0, Array.IndexOf(LangKeys, _settings.Language));
        ApiUrlBox.Text = _settings.ApiBaseUrl;
        VideoApiUrlBox.Text = _settings.VideoBaseUrl;
        _initialized = true;

        LanguageCombo.SelectionChanged += OnLanguageChanged;

        Loaded += async (_, _) =>
        {
            try
            {
                var coreVersion = await _core.GetVersionAsync();
                var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
                VersionText.Text = $"anibel-core {coreVersion.Version} · anibel-windows {appVersion}";
            }
            catch (Exception ex)
            {
                VersionText.Text = ex.Message;
            }
        };
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
            await App.Services.GetRequiredService<ICoreClient>().CallAsync<System.Text.Json.JsonElement>("clearPlaybackAssets");
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
