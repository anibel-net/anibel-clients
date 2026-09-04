using Anibel.App.Core;
using Anibel.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Anibel.App.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly ICoreClient _core;
    private readonly SessionService _session;

    public ProfileViewModel(ICoreClient core, SessionService session)
    {
        _core = core;
        _session = session;
        RefreshFlags();
    }

    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isLoggedIn;
    [ObservableProperty] private string hello = "";
    [ObservableProperty] private string? avatarUrl;
    [ObservableProperty] private bool loginError;
    [ObservableProperty] private long favoriteCount;
    [ObservableProperty] private long inProgressCount;
    [ObservableProperty] private long doneCount;
    [ObservableProperty] private long plannedCount;
    [ObservableProperty] private long droppedCount;
    [ObservableProperty] private bool hubBusy;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    public void RefreshFlags()
    {
        IsLoggedIn = _session.HasSession;
        if (IsLoggedIn)
        {
            Hello = _session.Username ?? "";
            AvatarUrl = _session.Avatar;
        }
        else
        {
            Hello = "";
            AvatarUrl = null;
        }
    }

    [RelayCommand]
    public Task LoadPersonalAsync()
    {
        RefreshFlags();
        return IsLoggedIn ? LoadHubAsync() : Task.CompletedTask;
    }

    public async Task LoadHubAsync()
    {
        if (!_session.HasSession || string.IsNullOrEmpty(_session.Username))
        {
            return;
        }
        HubBusy = true;
        try
        {
            var user = _session.Username;
            var fav = await _core.FavoritesAsync(user, null, 0, 1);
            FavoriteCount = fav.TotalDocs;
            long watching = 0, watched = 0, planned = 0, dropped = 0;
            foreach (var type in new[] { "anime", "manga", "cinema", "games", "books" })
            {
                try
                {
                    var s = await _core.StatusAsync(user, type);
                    if (s is null)
                    {
                        continue;
                    }
                    watching += s.Watching ?? 0;
                    watched += s.Watched ?? 0;
                    planned += s.Planned ?? 0;
                    dropped += s.Dropped ?? 0;
                }
                catch (Exception ex)
                {
                    // one media type missing counters must not hide the rest
                    Diag.Log($"profile hub: status({type}) FAILED {ex.Message}");
                }
            }
            InProgressCount = watching;
            DoneCount = watched;
            PlannedCount = planned;
            DroppedCount = dropped;
        }
        catch (Exception ex)
        {
            Diag.Log($"profile hub: {ex.Message}");
        }
        finally
        {
            HubBusy = false;
        }
    }

    [RelayCommand]
    public async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            LoginError = true;
            StatusMessage = Strings.EnterLoginAndPassword;
            return;
        }
        IsBusy = true;
        LoginError = false;
        StatusMessage = null;
        try
        {
            var user = await _core.LoginAsync(Username.Trim(), Password);
            if (string.IsNullOrEmpty(user.Token))
            {
                LoginError = true;
                StatusMessage = Strings.NoTokenTryAgain;
                return;
            }
            _session.Save(user.Username, user.Id, user.Avatar, user.Token);
            WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
            StatusMessage = null;
            Password = "";
            RefreshFlags();
        }
        catch (Exception ex)
        {
            LoginError = true;
            StatusMessage = Ui.DisplayMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        try { await _core.LogoutAsync(); }
        catch { /* local logout even if network fails */ }
        _session.Clear();
        WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
        RefreshFlags();
    }
}
