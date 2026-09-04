using Anibel.App.Core;
using Anibel.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anibel.App.ViewModels;

public sealed record ProfileListArgs(string Kind, string Title);

public partial class ProfileListViewModel : ObservableObject
{
    private readonly ICoreClient _core;
    private readonly SessionService _session;
    private int _generation;

    public ProfileListViewModel(ICoreClient core, SessionService session)
    {
        _core = core;
        _session = session;
        Items = new IncrementalMediaCollection(() => Task.FromResult(0));
    }

    public IncrementalMediaCollection Items { get; }

    [ObservableProperty] private string title = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private string emptyHint = Strings.EmptyList;
    [ObservableProperty] private string? statusMessage;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool ShowErrorState => HasStatus && IsEmpty && !IsBusy;
    public bool ShowEmptyState => IsEmpty && !HasStatus && !IsBusy;
    partial void OnStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowErrorState));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
    partial void OnIsEmptyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowErrorState));
    }
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowErrorState));
    }

    public async Task LoadAsync(ProfileListArgs args)
    {
        Title = args.Title;
        var generation = ++_generation;
        IsBusy = true;
        IsEmpty = false;
        StatusMessage = null;
        Items.ReplaceWith([]);
        await Task.Yield();
        try
        {
            var username = _session.Username;
            if (string.IsNullOrEmpty(username))
            {
                EmptyHint = Strings.LoginToSeeList;
                IsEmpty = true;
                return;
            }

            IReadOnlyList<MediaCard> docs;
            if (args.Kind == "favorites")
            {
                var page = await _core.FavoritesAsync(username, null, 0, 120);
                docs = page.Docs;
            }
            else
            {
                var page = await _core.MarksAsync(username, null, 0, 200);
                docs = page.Docs
                    .Where(e => e.Media is not null && Matches(e.Status, args.Kind))
                    .Select(e => e.Media!)
                    .ToList();
            }
            if (generation != _generation)
            {
                return;
            }
            Items.ReplaceWith(docs);
            IsEmpty = Items.Count == 0;
            EmptyHint = Strings.EmptyList;
        }
        catch (Exception ex)
        {
            if (generation != _generation)
            {
                return;
            }
            StatusMessage = Ui.DisplayMessage(ex);
            IsEmpty = true;
            EmptyHint = StatusMessage;
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
            }
        }
    }

    public static bool Matches(string? status, string kind)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return kind switch
        {
            "inprogress" => s is "watching" or "reading" or "playing",
            "done" => s is "watched" or "read" or "played",
            "planned" => s is "planned",
            "dropped" => s is "dropped",
            _ => false,
        };
    }
}
