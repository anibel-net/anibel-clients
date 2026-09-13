using System.Text.Json;
using Anibel.App.Services;
using Xunit;
namespace Anibel.App.Tests;

public class DownloadAdapterTests
{
    [Fact]
    public async Task Refresh_updates_existing_rows_and_uses_core_flags()
    {
        var row = new DownloadItem { Id = "one", Status = DownloadStatus.Completed, CanPlay = false, CanRetry = true };
        var snapshot = new DownloadService.Snapshot("core-root", [row]);
        var core = new FakeCoreClient { Handler = (op, args, ct) => Task.FromResult<object?>(snapshot) };
        var service = new DownloadService(core);
        await service.RefreshAsync();
        var original = Assert.Single(service.Items);
        Assert.False(original.CanPlay);
        Assert.True(original.CanRetry);
        Assert.Equal("core-root", service.Root);
        snapshot = new("core-root", [new DownloadItem { Id = "two" }, new DownloadItem { Id = "one", Status = DownloadStatus.Completed, CanPlay = true }]);
        await service.RefreshAsync();
        Assert.Same(original, service.Items[1]);
        Assert.True(original.CanPlay);
        snapshot = new("core-root", []);
        await service.RefreshAsync();
        Assert.Empty(service.Items);
    }
    [Fact]
    public async Task Commands_send_ids_and_refresh_the_core_snapshot()
    {
        var row = new DownloadItem { Id = "rust-id" };
        var core = new FakeCoreClient { Handler = (op, args, ct) => Task.FromResult<object?>(op == "downloads" ? new DownloadService.Snapshot("root", [row]) : JsonSerializer.SerializeToElement(new { })) };
        var service = new DownloadService(core);
        await service.Cancel(row);
        await service.Retry(row);
        await service.Delete(row);
        await service.CopyToFolderAsync(row, "chosen-folder");
        Assert.Equal(new[] { "cancel", "retry", "delete" }, core.Calls.Where(c => c.Op == "downloadChange").Select(c => c.Args.GetProperty("action").GetString()));
        Assert.All(core.Calls.Where(c => c.Op != "downloads"), c => Assert.Equal("rust-id", c.Args.GetProperty("id").GetString()));
        Assert.Equal("chosen-folder", core.Calls.Last().Args.GetProperty("destination").GetString());
    }
}

public class CoreStateAdapterTests
{
    [Fact]
    public async Task Session_follows_core_revision_and_clears_expired_credentials()
    {
        var core = new FakeCoreClient { Session = new(2, true, "alice", "1", null) };
        var credentials = new MemoryCredentialStore { Value = new("1", "alice", null, "user", "secret", null) };
        var session = new SessionService(core, credentials);
        await session.RefreshAsync();
        Assert.True(session.HasSession);
        core.Session = new(1, false, null, null, null);
        await session.RefreshAsync();
        Assert.True(session.HasSession);
        Assert.NotNull(credentials.Value);
        core.Session = new(3, false, null, null, null);
        await session.RefreshAsync();
        Assert.False(session.HasSession);
        Assert.Null(credentials.Value);
    }
    [Fact]
    public async Task Search_history_displays_core_result_without_local_filtering()
    {
        var core = new FakeCoreClient { Handler = (op, args, ct) => Task.FromResult<object?>(new[] { "core choice" }) };
        var history = new SearchHistory(core);
        await history.Add(" x ");
        Assert.Equal("core choice", Assert.Single(history.Items));
        Assert.Equal(" x ", core.Calls[0].Args.GetProperty("query").GetString());
        await history.Remove("core choice");
        Assert.Equal("remove", core.Calls[1].Args.GetProperty("action").GetString());
    }
}
