using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Anibel.App.Views;

public sealed record CatalogArgs(string MediaType, string Title);

public sealed partial class CatalogPage : Page
{
    public CatalogViewModel Vm { get; }

    public CatalogPage()
    {
        Vm = App.Services.GetRequiredService<CatalogViewModel>();
        InitializeComponent();
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CatalogViewModel.ShowLanguageFilters) or null)
            {
                SyncLanguageFilters();
            }
        };
        SyncLanguageFilters();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is CatalogArgs args)
        {
            await Vm.OpenAsync(args.MediaType, args.Title);
            SyncLanguageFilters();
        }
    }

    private void SyncLanguageFilters()
    {
        LanguageFilters.Visibility = Vm.ShowLanguageFilters ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGridToggle(object sender, RoutedEventArgs e)
    {
        Vm.IsGridMode = true;
        GridToggle.IsChecked = true;
        ListToggle.IsChecked = false;
    }

    private void OnListToggle(object sender, RoutedEventArgs e)
    {
        Vm.IsGridMode = false;
        GridToggle.IsChecked = false;
        ListToggle.IsChecked = true;
    }

    private void OnMediaClick(object? sender, MediaCard card)
    {
        Frame.Navigate(typeof(MediaDetailsPage), new MediaDetailsArgs(card.Slug, card.MediaType));
    }

    private async void OnLoadMore(object? sender, EventArgs e) => await Vm.LoadMoreAsync();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await Vm.ForceRefreshAsync();

    private void OnCopyStatusClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(Vm.StatusMessage);
}
