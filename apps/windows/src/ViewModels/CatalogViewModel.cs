using System.Collections.ObjectModel;
using Anibel.App.Core;
using Anibel.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anibel.App.ViewModels;

public partial class FilterChoice : ObservableObject
{
    public required string Value { get; init; }
    public required string Label { get; init; }

    [ObservableProperty] private bool isSelected;
}

public sealed class CatalogFilter : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required FilterChoice[] Choices { get; init; }
    public string Summary
    {
        get
        {
            var count = Choices.Count(c => c.IsSelected);
            return count > 0 ? $"{Label} · {count}" : Label;
        }
    }
    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));
}

public enum CatalogFilterState { Loading, Ready, Failed }

public partial class CatalogViewModel : ObservableObject
{
    private readonly ICoreClient _core;
    private long _offset;
    private int _generation;
    private bool _suppressFilter;
    private CancellationTokenSource? _filterCts;
    private object? _filterRequest;

    public CatalogViewModel(ICoreClient core)
    {
        _core = core;
        Items = new IncrementalMediaCollection(LoadMorePageAsync);
    }

    public IncrementalMediaCollection Items { get; }
    public ObservableCollection<CatalogFilter> Filters { get; } = new();

    [ObservableProperty] private string mediaType = "anime";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCount))] private string stats = "";
    public bool HasCount => Stats.Length > 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadingFilters), nameof(AreFiltersReady), nameof(HaveFiltersFailed))]
    private CatalogFilterState filterState = CatalogFilterState.Loading;
    public bool IsLoadingFilters => FilterState == CatalogFilterState.Loading;
    public bool AreFiltersReady => FilterState == CatalogFilterState.Ready;
    public bool HaveFiltersFailed => FilterState == CatalogFilterState.Failed;
    [ObservableProperty] private string emptyHint = Strings.NothingFound;
    [ObservableProperty] private bool isEmptyVisible;
    [ObservableProperty] private bool isGridMode = true;
    [ObservableProperty] private int selectedStatusIndex;
    [ObservableProperty] private bool chinaSelected;
    public bool ShowChinaFilter => MediaType == "anime";
    public bool ShowStatusFilter => MediaType != "games";
    [ObservableProperty] private bool subSelected;
    [ObservableProperty] private bool dubSelected;
    [ObservableProperty] private bool showLanguageFilters;
    [ObservableProperty] private string? statusMessage;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool ShowErrorState => HasStatus && IsEmptyVisible;
    public bool ShowEmptyState => IsEmptyVisible && !HasStatus;
    partial void OnStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowErrorState));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
    partial void OnIsEmptyVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowErrorState));
    }

    public async Task OpenAsync(string type, string titleText)
    {
        if (MediaType == type && Items.Count > 0)
        {
            return;
        }
        _filterCts?.Cancel();
        _filterRequest = null;
        FilterState = CatalogFilterState.Loading;
        _suppressFilter = true;
        Filters.Clear();
        SelectedStatusIndex = 0;
        ChinaSelected = SubSelected = DubSelected = false;
        _suppressFilter = false;
        MediaType = type;
        OnPropertyChanged(nameof(ShowChinaFilter));
        OnPropertyChanged(nameof(ShowStatusFilter));
        Title = titleText;
        ApplyTypeFilters(type);
        BeginImmediateReset();
        await Task.Yield();
        await Task.WhenAll(LoadFilterOptionsAsync(), FetchPageAsync(_generation, reset: true));
    }

    private void BeginImmediateReset()
    {
        _generation++;
        _offset = 0;
        HasMore = false;
        Items.HasMoreItems = false;
        Items.ReplaceWith([]);
        IsBusy = true;
        Stats = "";
        IsEmptyVisible = false;
        IsLoadingMore = false;
    }

    private void ApplyTypeFilters(string type)
    {
        var kind = type.Trim().ToLowerInvariant();
        // Sub/dub only exist on video catalogs. Manga, books, and games do not.
        ShowLanguageFilters = kind is "anime" or "cinema";
        if (!ShowLanguageFilters)
        {
            _suppressFilter = true;
            SubSelected = false;
            DubSelected = false;
            _suppressFilter = false;
        }
    }

    public async Task LoadFilterOptionsAsync()
    {
        var request = _filterRequest = new object();
        FilterState = CatalogFilterState.Loading;
        try
        {
            var type = MediaType;
            using var reload = CoreRequestScope.Reload();
            var filters = await _core.FiltersAsync(type);
            if (type != MediaType || !ReferenceEquals(request, _filterRequest)) return;
            Filters.Clear();
            if (MediaType != "games") AddFilter("type", "Тып", filters.Types, Ui.ContentType);
            AddFilter("year", "Год", filters.Years?.OrderByDescending(y => y).Select(y => y.ToString()));
            AddFilter("genres", "Жанры", filters.Genres, Ui.Genre);
            if (MediaType == "anime") AddFilter("studies", "Студыі", filters.Studios);
            AddFilter("translators", "Перакладчыкі", filters.Translators);
            AddFilter("editors", "Рэдактары", filters.Editors);
            AddFilter("cleanners", "Клінеры", filters.Cleanners);
            AddFilter("typpers", "Тайперы", filters.Typpers);
            AddFilter("programmers", "Праграмісты", filters.Programmers);
            if (ShowLanguageFilters) AddFilter("dubbers", "Даберы", filters.Dubbers);
            AddFilter("audioEngineers", "Гукаапрацоўка", filters.AudioEngineers);
            FilterState = CatalogFilterState.Ready;
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(request, _filterRequest)) return;
            FilterState = CatalogFilterState.Failed;
            Diag.Log($"CatalogViewModel: filters FAILED {ex}");
        }
    }

    private void AddFilter(string key, string label, IEnumerable<string>? values, Func<string, string>? display = null)
    {
        var choices = (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct()
            .Select(v => new FilterChoice { Value = v, Label = display?.Invoke(v) ?? v }).ToArray();
        if (choices.Length == 0) return;
        var filter = new CatalogFilter { Key = key, Label = label, Choices = choices };
        foreach (var choice in choices)
            choice.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(FilterChoice.IsSelected)) return;
                filter.RefreshSummary();
                if (!_suppressFilter) ScheduleFilterRefresh();
            };
        Filters.Add(filter);
    }

    public void ResetFilters()
    {
        _suppressFilter = true;
        foreach (var choice in Filters.SelectMany(f => f.Choices)) choice.IsSelected = false;
        SelectedStatusIndex = 0;
        ChinaSelected = SubSelected = DubSelected = false;
        _suppressFilter = false;
        ScheduleFilterRefresh();
    }

    partial void OnChinaSelectedChanged(bool value)
    {
        if (!_suppressFilter) ScheduleFilterRefresh();
    }

    partial void OnSelectedStatusIndexChanged(int value)
    {
        if (!_suppressFilter) ScheduleFilterRefresh();
    }
    partial void OnSubSelectedChanged(bool value)
    {
        if (!_suppressFilter) ScheduleFilterRefresh();
    }
    partial void OnDubSelectedChanged(bool value)
    {
        if (!_suppressFilter) ScheduleFilterRefresh();
    }
    private void ScheduleFilterRefresh()
    {
        if (_suppressFilter)
        {
            return;
        }
        BeginImmediateReset();
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;
        var gen = _generation;
        _ = RunFilterAsync(gen, token);
    }

    private async Task RunFilterAsync(int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(80, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await FetchPageAsync(generation, reset: true);
    }

    private object? BuildFilters()
    {
        var langs = new List<string>();
        if (ShowLanguageFilters)
        {
            if (SubSelected) langs.Add("sub");
            if (DubSelected) langs.Add("dub");
        }

        var result = new Dictionary<string, object>();
        foreach (var filter in Filters)
        {
            var selected = filter.Choices.Where(c => c.IsSelected).Select(c => c.Value).ToArray();
            if (selected.Length == 0) continue;
            result[filter.Key] = filter.Key == "year"
                ? selected.Select(long.Parse).ToArray() : selected;
        }
        if (langs.Count > 0) result["language"] = langs.ToArray();
        if (ShowStatusFilter && SelectedStatusIndex is 1 or 2)
            result["status"] = SelectedStatusIndex == 1 ? "ongoing" : "finished";
        if (ShowChinaFilter && ChinaSelected) result["country"] = "china";
        return result;
    }

    [RelayCommand]
    public Task RefreshAsync(bool reset = true)
    {
        if (reset)
        {
            ScheduleFilterRefresh();
            return Task.CompletedTask;
        }
        return FetchPageAsync(_generation, reset: false);
    }

    public async Task ForceRefreshAsync()
    {
        using var _ = CoreRequestScope.Reload();
        BeginImmediateReset();
        await FetchPageAsync(_generation, reset: true);
    }

    private async Task FetchPageAsync(int generation, bool reset)
    {
        var offsetUsed = reset ? 0 : _offset;
        if (!reset)
        {
            IsLoadingMore = true;
        }
        try
        {
            var page = await _core.MediaListAsync(MediaType, offsetUsed, 60, BuildFilters());
            if (generation != _generation)
            {
                return;
            }
            _offset = page.NextOffset;
            if (reset)
            {
                Items.ReplaceWith(page.Docs);
            }
            else
            {
                Items.AppendRange(page.Docs);
            }
            HasMore = page.HasMore;
            Items.HasMoreItems = HasMore;
            Stats = page.TotalDocs.ToString("N0");
            EmptyHint = Strings.NothingFoundHint;
            IsEmptyVisible = Items.Count == 0;
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            if (generation != _generation)
            {
                return;
            }
            _offset = offsetUsed;
            StatusMessage = Ui.DisplayMessage(ex);
            EmptyHint = StatusMessage;
            IsEmptyVisible = reset && Items.Count == 0;
            Items.HasMoreItems = false;
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
                IsLoadingMore = false;
            }
        }
    }

    [RelayCommand]
    public Task LoadMoreAsync() => LoadMorePageAsync();

    private async Task<int> LoadMorePageAsync()
    {
        if (!HasMore || IsBusy || IsLoadingMore)
        {
            Items.HasMoreItems = HasMore && !IsBusy;
            return 0;
        }
        var before = Items.Count;
        await RefreshAsync(reset: false);
        return Math.Max(0, Items.Count - before);
    }
}
