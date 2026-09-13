using Anibel.App.Services;
using Anibel.App.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace Anibel.App.Views;

public sealed record ProfileArgs(string Username);

public sealed partial class ProfilePage : Page, IRecipient<SessionChangedMessage>
{
    public ProfileViewModel Vm { get; }
    private string? _username;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _username = (e.Parameter as ProfileArgs)?.Username;
        base.OnNavigatedTo(e);
    }

    public ProfilePage()
    {
        Vm = App.Services.GetRequiredService<ProfileViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await Vm.LoadProfileAsync(_username);
            SyncChrome();
            if (Vm.IsOwn)
            {
                await Vm.LoadHubAsync();
                SyncCounts();
                ShowHub();
            }
        };
        Unloaded += (_, _) => WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.RegisterAll(this);
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProfileViewModel.IsLoggedIn)
                or nameof(ProfileViewModel.Profile)
                or nameof(ProfileViewModel.IsOwn)
                or nameof(ProfileViewModel.Hello)
                or nameof(ProfileViewModel.AvatarUrl)
                or nameof(ProfileViewModel.IsBusy)
                or nameof(ProfileViewModel.StatusMessage)
                or nameof(ProfileViewModel.LoginError)
                or nameof(ProfileViewModel.FavoriteCount)
                or nameof(ProfileViewModel.InProgressCount)
                or nameof(ProfileViewModel.DoneCount)
                or nameof(ProfileViewModel.PlannedCount)
                or nameof(ProfileViewModel.DroppedCount)
                or null)
            {
                SyncChrome();
            }
        };
    }

    public void Receive(SessionChangedMessage message) => _ = DispatcherQueue.TryEnqueue(async () =>
    {
        await Vm.LoadProfileAsync(_username);
        SyncChrome();
    });

    private void SyncChrome()
    {
        LoginHost.Visibility = !Vm.IsLoggedIn && _username is null ? Visibility.Visible : Visibility.Collapsed;
        AccountHost.Visibility = Vm.Profile is not null ? Visibility.Visible : Visibility.Collapsed;
        OwnActions.Visibility = ProfileBar.Visibility = PersonalContent.Visibility = Vm.IsOwn ? Visibility.Visible : Visibility.Collapsed;
        var profile = Vm.Profile;
        HelloText.Text = string.IsNullOrWhiteSpace(profile?.DisplayName) ? profile?.Username ?? "" : profile.DisplayName;
        HandleText.Text = profile is null ? "" : "@" + profile.Username;
        BioText.Text = profile?.Bio ?? "";
        BioText.Visibility = string.IsNullOrWhiteSpace(profile?.Bio) ? Visibility.Collapsed : Visibility.Visible;
        Avatar.DisplayName = HelloText.Text;
        Avatar.ProfilePicture = ImageSource(profile?.Avatar, 160);
        Wallpaper.Source = ImageSource(profile?.Wallpaper, 1200);
        Wallpaper.Visibility = Wallpaper.Source is null ? Visibility.Collapsed : Visibility.Visible;
        ProfileError.Message = Vm.StatusMessage ?? "";
        ProfileError.IsOpen = !Vm.LoginError && Vm.HasStatus;
        ProfileLoading.IsActive = Vm.IsBusy && (Vm.IsLoggedIn || _username is not null);
        ProfileLoading.Visibility = ProfileLoading.IsActive ? Visibility.Visible : Visibility.Collapsed;
        LoginButton.IsEnabled = !Vm.IsBusy;
        LoginProgress.Visibility = Vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        LoginErrorState.Message = Vm.StatusMessage ?? "";
        LoginErrorState.Visibility = Vm.LoginError && Vm.HasStatus ? Visibility.Visible : Visibility.Collapsed;
        SyncCounts();
    }

    private static BitmapImage? ImageSource(string? url, int width) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? new BitmapImage(uri) { DecodePixelWidth = width } : null;

    private async void OnEditProfileClick(object sender, RoutedEventArgs e)
    {
        if (!Vm.IsOwn || Vm.Profile is null) return;
        var editor = new ProfileEditView(Vm.Profile);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Рэдагаваць профіль", Content = editor,
            PrimaryButtonText = "Захаваць", CloseButtonText = "Скасаваць",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            editor.IsEnabled = false;
            try { await Vm.UpdateProfileAsync(editor.Patch); }
            catch (Exception ex) { editor.ShowError(Ui.DisplayMessage(ex)); args.Cancel = true; }
            finally { editor.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        await dialog.ShowAsync();
    }

    private void SyncCounts()
    {
        FavCountText.Text = CountLabel(Vm.FavoriteCount);
        WatchCountText.Text = CountLabel(Vm.InProgressCount);
        DoneCountText.Text = CountLabel(Vm.DoneCount);
        PlanCountText.Text = CountLabel(Vm.PlannedCount);
        DropCountText.Text = CountLabel(Vm.DroppedCount);
    }

    private static string CountLabel(long n) => Strings.RecordsCount(n);

    private void OnCopyLoginErrorClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(Vm.StatusMessage);

    private async void OnLoginClick(object sender, RoutedEventArgs e) => await SubmitLoginAsync();

    private async void OnLoginKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SubmitLoginAsync();
        }
    }

    private async Task SubmitLoginAsync()
    {
        Vm.Username = UsernameBox.Text;
        Vm.Password = PasswordBox.Password;
        await Vm.LoginAsync();
        PasswordBox.Password = "";
        SyncChrome();
        if (Vm.IsLoggedIn)
        {
            await Vm.LoadProfileAsync(_username);
            await Vm.LoadHubAsync();
            SyncCounts();
            ShowHub();
        }
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        await Vm.LogoutAsync();
        UsernameBox.Text = "";
        PasswordBox.Password = "";
        SyncChrome();
    }

    private void OnProfileBarChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag)
        {
            OpenTab(tag);
        }
    }

    private void OnHubCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
        {
            return;
        }
        foreach (var item in ProfileBar.Items.OfType<SelectorBarItem>())
        {
            if (item.Tag as string == tag)
            {
                ProfileBar.SelectedItem = item;
                break;
            }
        }
    }

    private void OpenTab(string tag)
    {
        if (tag == "hub")
        {
            ShowHub();
            return;
        }
        HubPanel.Visibility = Visibility.Collapsed;
        ProfileFrame.Visibility = Visibility.Visible;
        var title = tag switch
        {
            "favorites" => Strings.TabFavorites,
            "inprogress" => Strings.TabWatching,
            "done" => Strings.TabWatched,
            "planned" => Strings.TabPlanned,
            "dropped" => Strings.TabDropped,
            _ => tag,
        };
        ProfileFrame.Navigate(typeof(ProfileListPage), new ProfileListArgs(tag, title));
    }

    private void ShowHub()
    {
        HubPanel.Visibility = Visibility.Visible;
        ProfileFrame.Visibility = Visibility.Collapsed;
    }
}
