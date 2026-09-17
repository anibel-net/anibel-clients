using System.Collections.ObjectModel;
using Anibel.App.Core;
using Anibel.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Anibel.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly ICoreClient _core;
    private readonly SearchHistory? _history;
    private int _generation;

    public SearchViewModel(ICoreClient core, SearchHistory? history = null)
    {
        _core = core;
        _history = history;
    }

    public ObservableCollection<MediaCard> Items { get; } = new();

    [ObservableProperty] private string query = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCount))] private string stats = "";
    public bool HasCount => Stats.Length > 0;
    [ObservableProperty] private bool isEmptyHintVisible = true;
    [ObservableProperty] private bool isGridMode = true;
    [ObservableProperty] private string? statusMessage;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool ShowErrorState => HasStatus && !IsBusy && Items.Count == 0;
    public bool ShowIdleState => !IsBusy && !HasStatus && Items.Count == 0 && string.IsNullOrWhiteSpace(Query);
    public bool ShowNoResults => !IsBusy && !HasStatus && Items.Count == 0 && !string.IsNullOrWhiteSpace(Query);
    partial void OnStatusMessageChanged(string? value) => RaiseHints();
    partial void OnIsBusyChanged(bool value) => RaiseHints();
    partial void OnQueryChanged(string value) => RaiseHints();

    private void RaiseHints()
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowErrorState));
        OnPropertyChanged(nameof(ShowIdleState));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    [RelayCommand]
    public async Task SearchAsync(string? text = null)
    {
        var q = (text ?? Query).Trim();
        if (q.Length == 0)
        {
            return;
        }
        Query = q;
        var generation = ++_generation;
        Items.Clear();
        IsBusy = true;
        Stats = "";
        IsEmptyHintVisible = false;
        StatusMessage = null;
        await Task.Yield();
        try
        {
            if (_history is not null) await _history.Add(q);
            var results = await _core.SearchAsync(q, 120);
            if (generation != _generation)
            {
                return;
            }
            Items.Clear();
            foreach (var m in results)
            {
                Items.Add(m);
            }
            Stats = Items.Count.ToString("N0");
            IsEmptyHintVisible = Items.Count == 0;
            StatusMessage = null;
            RaiseHints();
        }
        catch (Exception ex)
        {
            if (generation != _generation)
            {
                return;
            }
            StatusMessage = Ui.DisplayMessage(ex);
            Stats = "";
            IsEmptyHintVisible = false;
            RaiseHints();
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
            }
        }
    }
}
