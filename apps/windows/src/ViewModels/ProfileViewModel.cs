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
    private object? _profileRequest;

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
    [ObservableProperty] private ProfileDto? profile;
    [ObservableProperty] private bool isOwn;

    public async Task LoadProfileAsync(string? username = null)
    {
        var request = _profileRequest = new object();
        var revision = _session.Revision;
        RefreshFlags();
        Profile = null;
        IsOwn = false;
        IsBusy = false;
        if (string.IsNullOrWhiteSpace(username) && !IsLoggedIn) return;
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var result = await _core.CallAsync<ProfileViewDto>("profile", new { username });
            if (!ReferenceEquals(request, _profileRequest) || revision != _session.Revision) return;
            Profile = result.Profile;
            IsOwn = result.IsOwn;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(request, _profileRequest) && revision == _session.Revision)
                StatusMessage = Ui.DisplayMessage(ex);
        }
        finally { if (ReferenceEquals(request, _profileRequest)) IsBusy = false; }
    }

    public async Task UpdateProfileAsync(object patch)
    {
        var result = await _core.CallAsync<ProfileDto>("updateProfile", patch);
        Profile = result;
        await _session.RefreshAsync();
        WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
    }

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
            var hub = await _core.CallAsync<ProfileHubDto>("profileHub");
            FavoriteCount = hub.Favorites;
            InProgressCount = hub.InProgress;
            DoneCount = hub.Done;
            PlannedCount = hub.Planned;
            DroppedCount = hub.Dropped;
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
            await _session.LoginAsync(Username.Trim(), Password);
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
        try { await _session.LogoutAsync(); }
        catch { /* local logout even if network fails */ }
        WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
        RefreshFlags();
    }
}
