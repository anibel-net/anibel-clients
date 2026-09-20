using Anibel.App.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Anibel.App.Playback;

/// <summary>Google Drive / external-embed pipeline (resource 1).</summary>
public sealed class WebView2Engine : IPlayerEngine
{
    private readonly Panel _host;
    private WebView2? _webView;
    private bool _disposed;
    private bool _readyRaised;

    public WebView2Engine(Panel host)
    {
        _host = host;
    }

    // The embed pipeline has no native transport, so it deliberately does not
    // implement IPlaybackControls: nothing can drive a host-embedded page.
    // Ready fires once the embed page finished navigating (see
    // OnNavigationCompleted) — not when Source is merely assigned.
    public event Action? Ready;
    public event Action<string>? Error;
    // IPlayerEngine requires these, but an embed page has no native transport
    // to observe — they are never raised. Keep them declared so hosts can
    // subscribe uniformly; suppress the compiler's "never used" warning.
#pragma warning disable CS0067
    public event Action? Ended;
    public event Action<double, double>? PositionChanged;
    public event Action<bool>? PauseChanged;
#pragma warning restore CS0067

    public bool IsInitialized => _webView?.CoreWebView2 is not null;
    public bool HasSubs => false;

    public void Load(PlaybackIntentDto intent, IReadOnlyList<string> subPaths)
    {
        // Fire-and-forget: failures surface through Error; never awaited here.
        _ = LoadAsync(intent, subPaths);
    }

    public async Task LoadAsync(PlaybackIntentDto intent, IReadOnlyList<string> subPaths, CancellationToken ct = default)
    {
        _ = subPaths;
        var url = intent.PageUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            Error?.Invoke("embed: empty url");
            return;
        }

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        webView.NavigationCompleted += OnNavigationCompleted;
        _host.Children.Add(webView);
        _webView = webView;

        try
        {
            ct.ThrowIfCancellationRequested();
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Anibel", "WebView2");
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, dataDir,
                new CoreWebView2EnvironmentOptions());
            ct.ThrowIfCancellationRequested();
            await webView.EnsureCoreWebView2Async(env);
            ct.ThrowIfCancellationRequested();
            webView.Source = new Uri(url);
            // Ready is raised from OnNavigationCompleted once the page has
            // finished (successfully) loading.
        }
        catch (OperationCanceledException)
        {
            RemoveWebView();
            throw;
        }
        catch (Exception ex)
        {
            RemoveWebView();
            Error?.Invoke($"WebView2: {ex.Message}");
        }
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // A page may navigate several times (redirects, embeds) — Ready only once.
        if (_readyRaised || _disposed)
        {
            return;
        }
        _readyRaised = true;
        if (e.IsSuccess)
        {
            Ready?.Invoke();
        }
        else
        {
            Error?.Invoke($"embed: navigation failed ({e.WebErrorStatus})");
        }
    }

    private void RemoveWebView()
    {
        if (_webView is { } view)
        {
            _host.Children.Remove(view);
            view.Close();
            _webView = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        RemoveWebView();
    }
}
