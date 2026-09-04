using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Anibel.App.Views;

public sealed partial class ProfileListPage : Page
{
    private ProfileListArgs? _args;
    public ProfileListViewModel Vm { get; }

    public ProfileListPage()
    {
        Vm = App.Services.GetRequiredService<ProfileListViewModel>();
        InitializeComponent();
        ListView.ItemsSource = Vm.Items;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ProfileListArgs args)
        {
            _args = args;
            await Vm.LoadAsync(args);
        }
    }

    private void OnMediaClick(object? sender, MediaCard card)
    {
        WeakReferenceMessenger.Default.Send(new OpenMediaMessage(card.Slug, card.MediaType));
    }

    private void OnCopyStatusClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(Vm.StatusMessage);

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (_args is not null)
        {
            await Vm.LoadAsync(_args);
        }
    }
}
