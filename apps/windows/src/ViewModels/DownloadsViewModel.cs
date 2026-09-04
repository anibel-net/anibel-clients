using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Anibel.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anibel.App.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    private readonly DownloadService _downloads;

    public DownloadsViewModel(DownloadService downloads)
    {
        _downloads = downloads;
        Items = downloads.Items;
        Items.CollectionChanged += OnItemsChanged;
        foreach (var item in Items)
        {
            item.PropertyChanged += OnItemChanged;
        }
        Rebuild();
    }

    public ObservableCollection<DownloadItem> Items { get; }
    public ObservableCollection<DownloadItem> Visible { get; } = new();

    [ObservableProperty] private string filter = "all";
    [ObservableProperty] private string query = "";
    [ObservableProperty] private string diskLabel = "";
    [ObservableProperty] private bool isEmpty;

    public string Root => _downloads.Root;

    partial void OnFilterChanged(string value) => Rebuild();
    partial void OnQueryChanged(string value) => Rebuild();

    public void Refresh()
    {
        DiskLabel = _downloads.Items.Count == 0
            ? ""
            : Strings.OnDisk(_downloads.TotalBytesLabel());
        Rebuild();
    }

    [RelayCommand]
    public void Cancel(DownloadItem? item)
    {
        if (item is not null) _downloads.Cancel(item);
        Refresh();
    }

    [RelayCommand]
    public void Retry(DownloadItem? item)
    {
        if (item is not null) _downloads.Retry(item);
        Refresh();
    }

    [RelayCommand]
    public void Delete(DownloadItem? item)
    {
        if (item is not null) _downloads.Delete(item);
        Refresh();
    }

    public Task CopyToFolderAsync(DownloadItem item, string folder, CancellationToken ct = default) =>
        _downloads.CopyToFolderAsync(item, folder, ct);

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (DownloadItem item in e.NewItems)
            {
                item.PropertyChanged += OnItemChanged;
            }
        }
        if (e.OldItems is not null)
        {
            foreach (DownloadItem item in e.OldItems)
            {
                item.PropertyChanged -= OnItemChanged;
            }
        }
        Rebuild();
        DiskLabel = _downloads.Items.Count == 0
            ? ""
            : Strings.OnDisk(_downloads.TotalBytesLabel());
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItem.Status)
            or nameof(DownloadItem.Progress)
            or nameof(DownloadItem.ProgressLabel)
            or nameof(DownloadItem.IsCompleted))
        {
            if (Filter != "all")
            {
                Rebuild();
            }
            DiskLabel = _downloads.Items.Count == 0
                ? ""
                : Strings.OnDisk(_downloads.TotalBytesLabel());
        }
    }

    private void Rebuild()
    {
        var q = Query.Trim();
        var want = Items.Where(i => MatchesFilter(i) && MatchesQuery(i, q)).ToList();
        for (var i = Visible.Count - 1; i >= 0; i--)
        {
            if (!want.Contains(Visible[i]))
            {
                Visible.RemoveAt(i);
            }
        }
        var insertAt = 0;
        foreach (var item in want)
        {
            var idx = Visible.IndexOf(item);
            if (idx < 0)
            {
                Visible.Insert(insertAt, item);
            }
            else if (idx != insertAt)
            {
                Visible.Move(idx, insertAt);
            }
            insertAt++;
        }
        IsEmpty = Visible.Count == 0;
    }

    private bool MatchesFilter(DownloadItem item) => Filter switch
    {
        "active" => item.IsActive,
        "ready" => item.IsCompleted,
        _ => true,
    };

    private static bool MatchesQuery(DownloadItem item, string q)
    {
        if (q.Length == 0)
        {
            return true;
        }
        return item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase)
            || item.KindLabel.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}
