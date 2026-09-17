using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Anibel.App.Views;

public sealed record SearchArgs(string? Query);

public sealed partial class SearchPage : Page
{
    public SearchViewModel Vm { get; }

    public SearchPage()
    {
        Vm = App.Services.GetRequiredService<SearchViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Vm.PropertyChanged -= OnSearchStateChanged;
        UpdateSearchLayout();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.PropertyChanged += OnSearchStateChanged;
        UpdateSearchLayout();
    }

    private void OnSearchStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.Query)) UpdateSearchLayout();
    }

    private void UpdateSearchLayout()
    {
        var idle = string.IsNullOrWhiteSpace(Vm.Query);
        Grid.SetRowSpan(SearchPanel, idle ? 2 : 1);
        SearchPanel.VerticalAlignment = idle ? VerticalAlignment.Center : VerticalAlignment.Top;
        SearchPanel.MaxWidth = idle ? 720 : double.PositiveInfinity;
        SearchPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        SearchBox.MaxWidth = idle ? 720 : double.PositiveInfinity;
        SearchIntro.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        SearchTools.Visibility = SearchResults.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is SearchArgs { Query: { Length: > 0 } query })
        {
            SearchBox.Text = query;
            await Vm.SearchAsync(query);
        }
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await Vm.SearchAsync(args.QueryText);
    }

    private void OnCopyStatusClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(Vm.StatusMessage);

    private async void OnRetrySearchClick(object sender, RoutedEventArgs e)
        => await Vm.SearchAsync(Vm.Query);

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
}
