using System.Collections.ObjectModel;
using Anibel.App.Core;
using Anibel.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anibel.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    public const int PageSize = 24;

    private readonly ICoreClient _core;
    private bool _loaded;
    private int _offset;
    private int _generation;

    public HomeViewModel(ICoreClient core)
    {
        _core = core;
    }

    public ObservableCollection<SlideDto> Slides { get; } = new();
    public ObservableCollection<MediaCard> Updates { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool updatesBusy;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool hasSlides;
    [ObservableProperty] private bool updatesEmpty;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private string updateType = "ALL";

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool ShowUpdatesError => HasStatus && UpdatesEmpty && !UpdatesBusy;
    public bool ShowUpdatesEmpty => UpdatesEmpty && !HasStatus && !UpdatesBusy;

    partial void OnStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowUpdatesError));
        OnPropertyChanged(nameof(ShowUpdatesEmpty));
    }
    partial void OnUpdatesEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUpdatesError));
        OnPropertyChanged(nameof(ShowUpdatesEmpty));
    }
    partial void OnUpdatesBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUpdatesError));
        OnPropertyChanged(nameof(ShowUpdatesEmpty));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            await Task.WhenAll(LoadSlidesAsync(), LoadUpdatesAsync(reset: true));
            _loaded = true;
        }
        catch (Exception ex)
        {
            StatusMessage = Ui.DisplayMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ForceRefreshAsync()
    {
        using var _ = ApiCache.Bypass();
        _loaded = false;
        Updates.Clear();
        await LoadAsync();
    }

    public async Task SetUpdateTypeAsync(string type)
    {
        if (string.Equals(UpdateType, type, StringComparison.OrdinalIgnoreCase) && Updates.Count > 0)
        {
            return;
        }
        UpdateType = type;
        Updates.Clear();
        UpdatesEmpty = false;
        UpdatesBusy = true;
        HasMore = false;
        await Task.Yield();
        await LoadUpdatesAsync(reset: true);
    }

    public Task LoadMoreAsync() => LoadUpdatesAsync(reset: false);

    private async Task LoadSlidesAsync()
    {
        try
        {
            var slides = await _core.SliderAsync(8);
            Slides.Clear();
            foreach (var s in slides)
            {
                if (s is null || string.IsNullOrWhiteSpace(s.Img))
                {
                    continue;
                }
                Slides.Add(s);
            }
            HasSlides = Slides.Count > 0;
        }
        catch (Exception ex)
        {
            HasSlides = false;
            StatusMessage = Ui.DisplayMessage(ex);
        }
    }

    private async Task LoadUpdatesAsync(bool reset)
    {
        var generation = ++_generation;
        if (reset)
        {
            _offset = 0;
        }
        if (reset)
        {
            Updates.Clear();
            UpdatesEmpty = false;
            UpdatesBusy = true;
        }
        try
        {
            var page = await _core.UpdatesAsync(UpdateType, _offset, PageSize);
            if (generation != _generation)
            {
                return;
            }
            if (reset)
            {
                Updates.Clear();
            }
            foreach (var m in page)
            {
                Updates.Add(m);
            }
            _offset += page.Length;
            HasMore = page.Length >= PageSize;
            UpdatesEmpty = Updates.Count == 0;
        }
        catch (Exception ex)
        {
            if (generation != _generation)
            {
                return;
            }
            if (reset)
            {
                Updates.Clear();
                UpdatesEmpty = true;
            }
            StatusMessage = Ui.DisplayMessage(ex);
        }
        finally
        {
            if (generation == _generation)
            {
                UpdatesBusy = false;
            }
        }
    }
}
