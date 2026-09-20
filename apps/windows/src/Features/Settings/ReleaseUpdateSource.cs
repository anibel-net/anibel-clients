using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Anibel.App.Services;

public interface IReleaseUpdateSource
{
    bool IsInstalled { get; }
    string? PendingVersion { get; }
    Task<string?> CheckAsync();
    Task DownloadAsync(Action<int> progress, CancellationToken ct);
    void PrepareRestart();
}

public sealed class ReleaseUpdateSource : IReleaseUpdateSource
{
    public static string Metadata(string key) => typeof(ReleaseUpdateSource).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";
    public static string CurrentVersion => typeof(ReleaseUpdateSource).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";
    public static string Repository => Metadata("UpdateRepository");
    public static bool IsPreview => Metadata("UpdateChannel").EndsWith("-preview", StringComparison.Ordinal);
    private readonly UpdateManager? _manager;
    private UpdateInfo? _available;
    public ReleaseUpdateSource()
    {
        if (Uri.TryCreate(Repository, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com")
            _manager = new UpdateManager(new GithubSource(Repository, null, IsPreview),
                new UpdateOptions { ExplicitChannel = Metadata("UpdateChannel"), AllowVersionDowngrade = false });
    }
    public bool IsInstalled => _manager?.IsInstalled == true;
    public string? PendingVersion => IsInstalled ? _manager!.UpdatePendingRestart?.Version.ToString() : null;
    public async Task<string?> CheckAsync()
    {
        _available = await _manager!.CheckForUpdatesAsync();
        return _available?.TargetFullRelease.Version.ToString();
    }
    public Task DownloadAsync(Action<int> progress, CancellationToken ct)
        => _manager!.DownloadUpdatesAsync(_available ?? throw new InvalidOperationException("No update selected."), progress, ct);
    public void PrepareRestart()
        => _manager!.WaitExitThenApplyUpdates(_manager.UpdatePendingRestart ?? throw new InvalidOperationException("No update is ready."), restart: true);
}
