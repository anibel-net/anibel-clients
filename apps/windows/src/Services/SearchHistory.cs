using Anibel.App.Core;
namespace Anibel.App.Services;

/// <summary>Display copy of core-owned saved search queries.</summary>
public sealed class SearchHistory(ICoreClient core)
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    public IReadOnlyList<string> Items { get; private set; } = [];
    public Task RefreshAsync() => SendAsync("list", "");
    public Task Add(string query) => SendAsync("add", query);
    public Task Remove(string query) => SendAsync("remove", query);
    private async Task SendAsync(string action, string query)
    {
        await _commands.WaitAsync();
        try
        {
            Items = await core.CallAsync<string[]>("searchHistory", new
            {
                action,
                query
            });
        }
        finally { _commands.Release(); }
    }
}
