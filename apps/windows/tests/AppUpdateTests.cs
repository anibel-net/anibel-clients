using Anibel.App.Services;
using Xunit;
namespace Anibel.App.Tests;
public class AppUpdateTests
{
    private sealed class Source : IReleaseUpdateSource
    {
        public bool IsInstalled { get; set; } = true;
        public string? PendingVersion { get; set; }
        public string? Available = "0.3.0";
        public int Checks, Downloads, Applies;
        public bool Fail;
        public TaskCompletionSource? Gate;
        public async Task<string?> CheckAsync() { Checks++; if (Gate is not null) await Gate.Task; if (Fail) throw new IOException("offline"); return Available; }
        public Task DownloadAsync(Action<int> progress, CancellationToken ct) { Downloads++; progress(100); PendingVersion = Available; return Task.CompletedTask; }
        public void ApplyAndRestart() { Applies++; }
    }
    [Fact]
    public async Task Downloads_once_and_waits_for_explicit_restart()
    {
        var source = new Source(); using var updates = new AppUpdateService(source);
        await updates.CheckAsync(); await updates.CheckAsync();
        Assert.Equal(AppUpdateState.Ready, updates.State);
        Assert.Equal(1, source.Downloads); Assert.Equal(0, source.Applies);
        updates.ApplyAndRestart(); Assert.Equal(1, source.Applies);
    }
    [Fact]
    public async Task Concurrent_checks_share_one_operation()
    {
        var source = new Source { Gate = new() }; using var updates = new AppUpdateService(source);
        var first = updates.CheckAsync(); var second = updates.CheckAsync();
        Assert.Equal(1, source.Checks);
        source.Gate.SetResult(); await Task.WhenAll(first, second);
        Assert.Equal(1, source.Downloads);
    }
    [Fact]
    public async Task Network_failure_can_be_retried()
    {
        var source = new Source { Fail = true }; using var updates = new AppUpdateService(source);
        await updates.CheckAsync(); Assert.Equal(AppUpdateState.Failed, updates.State);
        source.Fail = false; await updates.CheckAsync(); Assert.True(updates.IsReady);
    }
    [Fact]
    public async Task Portable_development_build_does_not_check_or_apply()
    {
        var source = new Source { IsInstalled = false }; using var updates = new AppUpdateService(source);
        updates.Start(); await updates.CheckAsync(); await updates.StopAsync();
        Assert.Equal(0, source.Checks); Assert.False(updates.CanCheck);
        Assert.Throws<InvalidOperationException>(() => updates.ApplyAndRestart());
    }
    [Fact]
    public async Task Pending_update_survives_restart_without_automatic_install()
    {
        var source = new Source { PendingVersion = "0.3.0" }; using var updates = new AppUpdateService(source);
        Assert.True(updates.IsReady); await updates.CheckAsync(); Assert.Equal(0, source.Checks); Assert.Equal(0, source.Applies);
    }
    [Fact]
    public async Task Shutdown_does_not_wait_for_network_or_start_download()
    {
        var source = new Source { Gate = new() }; using var updates = new AppUpdateService(source);
        var check = updates.CheckAsync(); await updates.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        source.Gate.SetResult(); await check; Assert.Equal(0, source.Downloads);
    }
    [Fact]
    public async Task No_new_version_has_a_clear_status_and_does_not_download()
    {
        var source = new Source { Available = null }; using var updates = new AppUpdateService(source);
        await updates.CheckAsync(); Assert.Equal(AppUpdateState.UpToDate, updates.State);
        Assert.Equal(0, source.Downloads); Assert.True(updates.CanCheck);
    }
}
