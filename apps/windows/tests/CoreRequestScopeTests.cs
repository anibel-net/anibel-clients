using Anibel.App.Core;
using Xunit;

namespace Anibel.App.Tests;

public sealed class CoreRequestScopeTests
{
    [Fact]
    public async Task Reload_scope_is_nested_and_does_not_change_parallel_calls()
    {
        Assert.False(CoreRequestScope.IsReload);
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var work = Task.Run(async () =>
        {
            using (CoreRequestScope.Reload())
            {
                Assert.True(CoreRequestScope.IsReload);
                var nested = CoreRequestScope.Reload();
                nested.Dispose();
                nested.Dispose();
                Assert.True(CoreRequestScope.IsReload);
                entered.SetResult();
                await release.Task;
                Assert.True(CoreRequestScope.IsReload);
            }
            Assert.False(CoreRequestScope.IsReload);
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.False(CoreRequestScope.IsReload); }
        finally { release.SetResult(); }
        await work;
    }
}
