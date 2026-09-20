using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Velopack.Logging;

if (args.Length is < 3 or > 4) throw new ArgumentException("Usage: UpdateSmoke FEED CURRENT_VERSION EXPECTED_VERSION [CHANNEL]");
var feed = Path.GetFullPath(args[0]);
var channel = args.Length == 4 ? args[3] : "win-x64";
var current = args[1];
var expected = args[2];
var updateExe = Environment.GetEnvironmentVariable("VPK_UPDATE_EXE") ?? Path.Combine(
    Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
    "vpk", "1.2.0", "vendor", "update.exe");
if (!File.Exists(updateExe)) throw new FileNotFoundException("Run dotnet tool restore or set VPK_UPDATE_EXE.", updateExe);
var root = Path.Combine(Path.GetTempPath(), "Anibel-update-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    foreach (var useDelta in new[] { false, true })
    {
        var cache = Path.Combine(root, useDelta ? "delta" : "full");
        Directory.CreateDirectory(cache);
        if (useDelta) File.Copy(Path.Combine(feed, $"Anibel.Net-{current}-{channel}-full.nupkg"), Path.Combine(cache, $"Anibel.Net-{current}-{channel}-full.nupkg"));
        var locator = new TestVelopackLocator("Anibel.Net", current, cache, root, root, updateExe, channel);
        var source = new TrackingSource(new DirectoryInfo(feed));
        var manager = new UpdateManager(source,
            new UpdateOptions { ExplicitChannel = channel }, locator);
        var update = await manager.CheckForUpdatesAsync() ?? throw new Exception("New release not found");
        if (update.TargetFullRelease.Version.ToString() != expected) throw new Exception("Wrong release selected");
        if (useDelta && update.DeltasToTarget.Length == 0) throw new Exception("Delta not selected");
        await manager.DownloadUpdatesAsync(update);
        if (useDelta && source.Downloaded.Any(a => a.Type == VelopackAssetType.Full)) throw new Exception("Delta reconstruction fell back to the full package");
        if (manager.UpdatePendingRestart?.Version.ToString() != expected) throw new Exception("Update was not staged");
        Console.WriteLine($"PASS {(useDelta ? "delta" : "full")} download, checksum verification, pending version {expected}");
    }
}
finally { Directory.Delete(root, recursive: true); }

sealed class TrackingSource(DirectoryInfo directory) : IUpdateSource
{
    private readonly SimpleFileSource _inner = new(directory);
    public List<VelopackAsset> Downloaded { get; } = [];
    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId, VelopackAsset? latestLocalRelease)
        => _inner.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);
    public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
    {
        Downloaded.Add(releaseEntry);
        return _inner.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }
}
