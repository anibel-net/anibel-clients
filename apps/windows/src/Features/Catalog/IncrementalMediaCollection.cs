using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Anibel.App.Core;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;

namespace Anibel.App.ViewModels;

/// <summary>
/// Observable list that GridView/ListView can page via
/// <see cref="ISupportIncrementalLoading"/> (WinUI edge loading).
/// Replace/append go through a single collection change so the grid does not
/// re-layout once per card.
/// </summary>
public sealed class IncrementalMediaCollection : ObservableCollection<MediaCard>, ISupportIncrementalLoading
{
    private readonly Func<Task<int>> _loadMore;
    private bool _busy;

    public IncrementalMediaCollection(Func<Task<int>> loadMore)
    {
        _loadMore = loadMore;
    }

    public bool HasMoreItems { get; set; }

    public void ReplaceWith(IEnumerable<MediaCard> source)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in source)
        {
            Items.Add(item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void AppendRange(IReadOnlyList<MediaCard> source)
    {
        if (source.Count == 0)
        {
            return;
        }
        CheckReentrancy();
        var start = Count;
        foreach (var item in source)
        {
            Items.Add(item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(source.Count == 1
            ? new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, source[0], start)
            : new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, source.ToList(), start));
    }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        => LoadAsync(count).AsAsyncOperation();

    private async Task<LoadMoreItemsResult> LoadAsync(uint count)
    {
        _ = count;
        if (!HasMoreItems || _busy)
        {
            return new LoadMoreItemsResult { Count = 0 };
        }
        _busy = true;
        try
        {
            var added = await _loadMore();
            return new LoadMoreItemsResult { Count = (uint)Math.Max(0, added) };
        }
        finally
        {
            _busy = false;
        }
    }
}
