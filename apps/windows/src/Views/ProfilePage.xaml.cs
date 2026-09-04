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

public sealed partial class ProfilePage : Page, IRecipient<SessionExpiredMessage>
{
    public ProfileViewModel Vm { get; }

    public ProfilePage()
    {
        Vm = App.Services.GetRequiredService<ProfileViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Vm.RefreshFlags();
            SyncChrome();
            if (Vm.IsLoggedIn)
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

    public void Receive(SessionExpiredMessage message) => _ = DispatcherQueue.TryEnqueue(() =>
    {
        Vm.RefreshFlags();
        SyncChrome();
    });

    private void SyncChrome()
    {
        LoginHost.Visibility = Vm.IsLoggedIn ? Visibility.Collapsed : Visibility.Visible;
        AccountHost.Visibility = Vm.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        HelloText.Text = Vm.Hello;
        Avatar.DisplayName = string.IsNullOrEmpty(Vm.Hello) ? Strings.Profile : Vm.Hello;
        if (Vm.AvatarUrl is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Avatar.ProfilePicture = new BitmapImage(uri);
        }
        else
        {
            Avatar.ProfilePicture = null;
        }
        LoginButton.IsEnabled = !Vm.IsBusy;
        LoginProgress.Visibility = Vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        LoginErrorState.Message = Vm.StatusMessage ?? "";
        LoginErrorState.Visibility = Vm.LoginError && Vm.HasStatus ? Visibility.Visible : Visibility.Collapsed;
        SyncCounts();
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
