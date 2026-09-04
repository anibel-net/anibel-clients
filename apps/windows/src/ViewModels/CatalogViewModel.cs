using System.Collections.ObjectModel;
using Anibel.App.Core;
using Anibel.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anibel.App.ViewModels;

public partial class GenreChip : ObservableObject
{
    public required string Value { get; init; }
    public required string Label { get; init; }

    [ObservableProperty] private bool isSelected;
}

public sealed class FilterOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public override string ToString() => Label;
}

public partial class CatalogViewModel : ObservableObject
{
    private readonly ICoreClient _core;
    private int _offset;
    private long _total;
    private int _generation;
    private bool _suppressFilter;
    private CancellationTokenSource? _filterCts;

    public CatalogViewModel(ICoreClient core)
    {
        _core = core;
        Items = new IncrementalMediaCollection(LoadMorePageAsync);
    }

    public IncrementalMediaCollection Items { get; }
    public ObservableCollection<GenreChip> Genres { get; } = new();
    public ObservableCollection<object> Years { get; } = new();
    public ObservableCollection<FilterOption> Countries { get; } = new();
    public ObservableCollection<FilterOption> ContentTypes { get; } = new();

    [ObservableProperty] private string mediaType = "anime";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private string stats = "";
    [ObservableProperty] private string emptyHint = Strings.NothingFound;
    [ObservableProperty] private bool isEmptyVisible;
    [ObservableProperty] private bool isGridMode = true;
    [ObservableProperty] private int selectedYearIndex;
    [ObservableProperty] private int selectedStatusIndex;
    [ObservableProperty] private int selectedCountryIndex;
    [ObservableProperty] private int selectedTypeIndex;
    [ObservableProperty] private bool subSelected;
    [ObservableProperty] private bool dubSelected;
    [ObservableProperty] private bool showLanguageFilters;
    [ObservableProperty] private bool showTypeFilter;
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
        MediaType = type;
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
        try
        {
            var filters = await _core.FiltersAsync(MediaType);
            _suppressFilter = true;
            Years.Clear();
            Years.Add(Strings.AllYears);
            foreach (var year in (filters.Years ?? []).OrderByDescending(x => x))
            {
                Years.Add(year);
            }
            SelectedYearIndex = 0;
            _suppressFilter = false;

            Genres.Clear();
            foreach (var genre in filters.Genres ?? [])
            {
                var chip = new GenreChip { Value = genre, Label = Ui.Genre(genre) };
                chip.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(GenreChip.IsSelected) && !_suppressFilter)
                    {
                        ScheduleFilterRefresh();
                    }
                };
                Genres.Add(chip);
            }

            _suppressFilter = true;
            Countries.Clear();
            Countries.Add(new FilterOption { Value = "", Label = Strings.AllCountries });
            foreach (var (value, label) in Ui.Countries)
            {
                Countries.Add(new FilterOption { Value = value, Label = label });
            }
            SelectedCountryIndex = 0;

            ContentTypes.Clear();
            ContentTypes.Add(new FilterOption { Value = "", Label = Strings.AllTypes });
            foreach (var t in filters.Types ?? [])
            {
                ContentTypes.Add(new FilterOption { Value = t, Label = Ui.ContentType(t) });
            }
            SelectedTypeIndex = 0;
            ShowTypeFilter = ContentTypes.Count > 1;
            _suppressFilter = false;
        }
        catch (CoreException ex)
        {
            Diag.Log($"CatalogViewModel: filters FAILED {ex}");
        }
    }

    partial void OnSelectedYearIndexChanged(int value)
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
    partial void OnSelectedCountryIndexChanged(int value)
    {
        if (!_suppressFilter) ScheduleFilterRefresh();
    }
    partial void OnSelectedTypeIndexChanged(int value)
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

        var genres = Genres.Where(g => g.IsSelected).Select(g => g.Value).ToList();
        int? year = SelectedYearIndex > 0 && Years[SelectedYearIndex] is long y ? (int)y : null;
        string? status = SelectedStatusIndex == 1 ? "ongoing" : SelectedStatusIndex == 2 ? "finished" : null;
        string? country = SelectedCountryIndex > 0 && SelectedCountryIndex < Countries.Count
            ? Countries[SelectedCountryIndex].Value
            : null;
        string? contentType = SelectedTypeIndex > 0 && SelectedTypeIndex < ContentTypes.Count
            ? ContentTypes[SelectedTypeIndex].Value
            : null;

        return new
        {
            year = year.HasValue ? new[] { year.Value } : null,
            status,
            language = langs.Count > 0 ? langs.ToArray() : null,
            genres = genres.Count > 0 ? genres.ToArray() : null,
            country,
            type = string.IsNullOrEmpty(contentType) ? null : new[] { contentType },
        };
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
        using var _ = ApiCache.Bypass();
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
            _offset = offsetUsed + page.Docs.Length;
            _total = page.TotalDocs;
            if (reset)
            {
                Items.ReplaceWith(page.Docs);
            }
            else
            {
                Items.AppendRange(page.Docs);
            }
            HasMore = _total > Items.Count && page.Docs.Length > 0;
            Items.HasMoreItems = HasMore;
            Stats = Strings.Total(_total);
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
