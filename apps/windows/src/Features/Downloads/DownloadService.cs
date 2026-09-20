using System.Collections.ObjectModel;
using System.Text.Json;
using Anibel.App.Core;

namespace Anibel.App.Services;

/// <summary>UI projection and command adapter. Rust owns jobs, files and status.</summary>
public sealed class DownloadService(ICoreClient core)
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    public ObservableCollection<DownloadItem> Items { get; } = [];
    public string Root { get; private set; } = "";
    public sealed record Snapshot(string Root, DownloadItem[] Items);

    public async Task RefreshAsync()
    {
        await _refresh.WaitAsync();
        try
        {
            var snapshot = await core.CallAsync<Snapshot>(CoreCommand.Downloads);
            Root = snapshot.Root;
            var incoming = snapshot.Items.Select(i => i.Id).ToHashSet();
            foreach (var old in Items.Where(i => !incoming.Contains(i.Id)).ToArray())
                Items.Remove(old);
            var byId = Items.ToDictionary(i => i.Id);
            for (var index = 0; index < snapshot.Items.Length; index++)
            {
                var source = snapshot.Items[index];
                byId.TryGetValue(source.Id, out var existing);
                if (existing is null)
                    Items.Insert(index, source);
                else
                {
                    existing.Apply(source);
                    if (!ReferenceEquals(Items[index], existing))
                        Items.Move(Items.IndexOf(existing), index);
                }
            }
        }
        finally { _refresh.Release(); }
    }
    public Task<DownloadItem> EnqueueEpisode(EpisodeDownloadRequest request)
        => Enqueue(request.AudioOnly ? "audio" : "video", request);
    public Task<DownloadItem> EnqueueChapter(ChapterDownloadRequest request) => Enqueue("manga", request);
    public Task<DownloadItem> EnqueueFile(FileDownloadRequest request) => Enqueue("file", request);
    private async Task<DownloadItem> Enqueue(string kind, object request)
    {
        var item = await core.CallAsync<DownloadItem>(CoreCommand.DownloadEnqueue, new
        {
            kind,
            request
        });
        await RefreshAsync();
        return item;
    }
    public Task Cancel(DownloadItem item) => Change(item, "cancel");
    public Task Retry(DownloadItem item) => Change(item, "retry");
    public Task Delete(DownloadItem item) => Change(item, "delete");
    private async Task Change(DownloadItem item, string action)
    {
        await core.CallAsync<JsonElement>(CoreCommand.DownloadChange, new
        {
            id = item.Id,
            action
        });
        await RefreshAsync();
    }
    public Task CopyToFolderAsync(DownloadItem item, string destination, CancellationToken ct = default)
        => core.CallAsync<JsonElement>(CoreCommand.DownloadExport, new
        {
            id = item.Id,
            destination
        }, ct);
    public string TotalBytesLabel() => DownloadItem.FormatBytes(Items.Sum(i => i.DiskBytes));
}
