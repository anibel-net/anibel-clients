using Anibel.App.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Anibel.App.Views;

/// <summary>
/// Poster collection: UniformGridLayout grid (fills leftover width) and a
/// compact list. Infinite scroll uses ListView incremental loading in list
/// mode and a ScrollViewer fallback in grid mode.
/// </summary>
public sealed partial class MediaCollectionView : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(object), typeof(MediaCollectionView),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty IsGridModeProperty = DependencyProperty.Register(
        nameof(IsGridMode), typeof(bool), typeof(MediaCollectionView),
        new PropertyMetadata(true, OnGridModeChanged));

    public static readonly DependencyProperty HasMoreProperty = DependencyProperty.Register(
        nameof(HasMore), typeof(bool), typeof(MediaCollectionView),
        new PropertyMetadata(false, OnHasMoreChanged));

    public static readonly DependencyProperty ShowSkeletonProperty = DependencyProperty.Register(
        nameof(ShowSkeleton), typeof(bool), typeof(MediaCollectionView),
        new PropertyMetadata(false, OnShowSkeletonChanged));

    private ScrollViewer? _scrollViewer;
    private DateTime _lastLoadMore = DateTime.MinValue;
    private bool _requestInFlight;
    private double _lastGridWidth = -1;

    public MediaCollectionView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AttachScrollViewer();
            RelayoutGrid();
        };
        GridScroll.Loaded += (_, _) =>
        {
            AttachScrollViewer();
            RelayoutGrid();
        };
        ListHost.Loaded += (_, _) => AttachScrollViewer();
        GridScroll.SizeChanged += OnGridSizeChanged;
        ListHost.SizeChanged += (_, _) => CheckNeedMore();
        var placeholders = new int[12];
        for (var i = 0; i < placeholders.Length; i++)
        {
            placeholders[i] = i;
        }
        SkeletonHost.ItemsSource = placeholders;
        GridRepeater.ElementPrepared += OnGridElementPrepared;
    }

    private static void OnGridElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not FrameworkElement fe)
        {
            return;
        }
        var item = ItemAt(sender.ItemsSource, args.Index);
        fe.DataContext = item;
        if (fe.Tag is null)
        {
            fe.Tag = item;
        }
    }

    private static object? ItemAt(object? source, int index)
    {
        if (index < 0)
        {
            return null;
        }
        if (source is System.Collections.IList list)
        {
            return index < list.Count ? list[index] : null;
        }
        if (source is System.Collections.IEnumerable enumerable)
        {
            var i = 0;
            foreach (var item in enumerable)
            {
                if (i++ == index)
                {
                    return item;
                }
            }
        }
        return null;
    }

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool IsGridMode
    {
        get => (bool)GetValue(IsGridModeProperty);
        set => SetValue(IsGridModeProperty, value);
    }

    public bool HasMore
    {
        get => (bool)GetValue(HasMoreProperty);
        set => SetValue(HasMoreProperty, value);
    }

    public bool ShowSkeleton
    {
        get => (bool)GetValue(ShowSkeletonProperty);
        set => SetValue(ShowSkeletonProperty, value);
    }

    public event EventHandler<MediaCard>? MediaClick;

    public event EventHandler? LoadMoreRequested;

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MediaCollectionView)d;
        view.GridRepeater.ItemsSource = e.NewValue;
        view.ListHost.ItemsSource = e.NewValue;
        view._lastLoadMore = DateTime.MinValue;
        view._requestInFlight = false;
        view.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                view.AttachScrollViewer();
                view.RelayoutGrid();
                view.CheckNeedMore();
            });
    }

    private static void OnGridModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MediaCollectionView)d;
        var grid = (bool)e.NewValue;
        view.GridScroll.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        view.ListHost.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        view.SkeletonHost.Visibility = view.ShowSkeleton && grid ? Visibility.Visible : Visibility.Collapsed;
        view._scrollViewer = null;
        view._lastGridWidth = -1;
        view.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                view.AttachScrollViewer();
                view.RelayoutGrid();
                view.CheckNeedMore();
            });
    }

    private static void OnShowSkeletonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MediaCollectionView)d;
        var show = e.NewValue is true;
        view.SkeletonHost.Visibility = show && view.IsGridMode ? Visibility.Visible : Visibility.Collapsed;
        view.GridScroll.Opacity = show ? 0 : 1;
        view.ListHost.Opacity = show ? 0 : 1;
        if (show)
        {
            view.RelayoutGrid();
        }
    }

    private static void OnHasMoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MediaCollectionView)d;
        if (e.NewValue is false)
        {
            view._requestInFlight = false;
        }
        view.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            view.CheckNeedMore);
    }

    private void AttachScrollViewer()
    {
        ScrollViewer? sv = IsGridMode ? GridScroll : FindScrollViewer(ListHost);
        if (ReferenceEquals(sv, _scrollViewer))
        {
            RelayoutGrid();
            return;
        }
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged -= OnViewChanged;
            _scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
        }
        _scrollViewer = sv;
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnViewChanged;
            _scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        }
        RelayoutGrid();
    }

    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RelayoutGrid();
        CheckNeedMore();
    }

    private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e) => RelayoutGrid();

    private void RelayoutGrid()
    {
        if (!IsGridMode)
        {
            return;
        }
        var width = GridScroll.ViewportWidth;
        if (width < 1)
        {
            width = GridScroll.ActualWidth;
        }
        if (width < 1)
        {
            width = ActualWidth;
        }
        if (width < 1)
        {
            return;
        }
        if (Math.Abs(width - _lastGridWidth) < 0.5)
        {
            return;
        }

        var measure = PosterGridLayout.ForViewport(width);
        var itemW = measure.ItemWidth;
        var itemH = measure.ItemHeight;
        var gap = PosterGridLayout.ColumnGap;

        GridLayout.MinItemWidth = itemW;
        GridLayout.MinItemHeight = itemH;
        GridLayout.MinColumnSpacing = gap;
        GridLayout.MinRowSpacing = gap;
        GridLayout.ItemsStretch = UniformGridLayoutItemsStretch.Fill;
        SkeletonLayout.MinItemWidth = itemW;
        SkeletonLayout.MinItemHeight = itemH;
        SkeletonLayout.MinColumnSpacing = gap;
        SkeletonLayout.MinRowSpacing = gap;
        SkeletonLayout.ItemsStretch = UniformGridLayoutItemsStretch.Fill;
        _lastGridWidth = width;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer self)
        {
            return self;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate)
        {
            _requestInFlight = false;
        }
        CheckNeedMore();
    }

    private void CheckNeedMore()
    {
        // Grid uses ItemsRepeater (no ISupportIncrementalLoading). ListView
        // still virtualizes; skip the fallback there when native loading works.
        if (!IsGridMode && ItemsSource is ISupportIncrementalLoading)
        {
            return;
        }
        if (!HasMore || ShowSkeleton)
        {
            return;
        }
        AttachScrollViewer();
        if (_scrollViewer is null)
        {
            return;
        }
        var scrollable = _scrollViewer.ScrollableHeight;
        var nearEnd = scrollable <= 48
            || _scrollViewer.VerticalOffset >= scrollable - 900;
        if (nearEnd)
        {
            RequestMore();
        }
    }

    private void RequestMore()
    {
        if (!HasMore || _requestInFlight)
        {
            return;
        }
        if (DateTime.UtcNow - _lastLoadMore < TimeSpan.FromMilliseconds(350))
        {
            return;
        }
        _lastLoadMore = DateTime.UtcNow;
        _requestInFlight = true;
        LoadMoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaCard card)
        {
            MediaClick?.Invoke(this, card);
        }
    }

    private void OnGridCardTapped(object sender, TappedRoutedEventArgs e)
    {
        // ItemsRepeater does not set DataContext; the card is on Tag via x:Bind.
        var card = (sender as FrameworkElement)?.Tag as MediaCard
            ?? (sender as FrameworkElement)?.DataContext as MediaCard;
        if (card is null)
        {
            return;
        }
        MediaClick?.Invoke(this, card);
        e.Handled = true;
    }
}
