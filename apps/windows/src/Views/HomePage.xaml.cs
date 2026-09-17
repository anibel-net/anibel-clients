using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Anibel.App.Views;

public sealed partial class HomePage : Page
{
    private readonly DispatcherQueueTimer _heroTimer;
    private bool _syncingHero;

    public HomeViewModel Vm { get; }

    public HomePage()
    {
        Vm = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
        UpdatesView.UsePageScrolling();
        _heroTimer = DispatcherQueue.CreateTimer();
        _heroTimer.Interval = TimeSpan.FromSeconds(7);
        _heroTimer.IsRepeating = true;
        _heroTimer.Tick += (_, _) => AdvanceHero();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _heroTimer.Stop();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _heroTimer.Stop();
        base.OnNavigatedFrom(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Vm.LoadAsync();
        SyncHeroPips();
        if (HeroCarousel.Items.Count > 0)
        {
            _heroTimer.Start();
        }
        foreach (var item in UpdateKindBar.Items.OfType<SelectorBarItem>())
        {
            if (item.Tag as string == Vm.UpdateType)
            {
                UpdateKindBar.SelectedItem = item;
                break;
            }
        }
    }

    private void SyncHeroPips()
    {
        var pages = Math.Max(1, Vm.Slides.Count);
        if (HeroPips.NumberOfPages != pages)
        {
            HeroPips.NumberOfPages = pages;
        }
        if (HeroCarousel.Items.Count == 0 || pages < 1)
        {
            return;
        }
        var index = Math.Clamp(Math.Max(0, HeroCarousel.SelectedIndex), 0, pages - 1);
        if (HeroPips.SelectedPageIndex != index)
        {
            _syncingHero = true;
            HeroPips.SelectedPageIndex = index;
            _syncingHero = false;
        }
    }

    private void AdvanceHero()
    {
        if (HeroCarousel.Items.Count == 0)
        {
            return;
        }
        var next = HeroCarousel.SelectedIndex + 1;
        HeroCarousel.SelectedIndex = next >= HeroCarousel.Items.Count ? 0 : next;
    }

    private void RestartHeroTimer()
    {
        _heroTimer.Stop();
        if (HeroCarousel.Items.Count > 1)
        {
            _heroTimer.Start();
        }
    }

    private void OnHeroSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingHero || HeroPips.NumberOfPages < 1)
        {
            return;
        }
        SyncHeroPips();
    }

    private void OnHeroPipsChanged(PipsPager sender, PipsPagerSelectedIndexChangedEventArgs args)
    {
        if (_syncingHero || HeroCarousel.Items.Count == 0 || sender.SelectedPageIndex < 0)
        {
            return;
        }
        _syncingHero = true;
        HeroCarousel.SelectedIndex = Math.Min(sender.SelectedPageIndex, HeroCarousel.Items.Count - 1);
        _syncingHero = false;
        RestartHeroTimer();
    }

    private void OnSlideTapped(object sender, TappedRoutedEventArgs e)
    {
        var slide = (sender as FrameworkElement)?.Tag as SlideDto
            ?? (sender as FrameworkElement)?.DataContext as SlideDto;
        if (slide is null || !TryParseMediaLink(slide.Link, out var slug, out var type))
        {
            return;
        }
        Frame.Navigate(typeof(MediaDetailsPage), new MediaDetailsArgs(slug, type));
    }

    private async void OnUpdateKindChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string type)
        {
            await Vm.SetUpdateTypeAsync(type);
        }
    }

    private async void OnLoadMore(object? sender, EventArgs e) => await Vm.LoadMoreAsync();

    private void OnCopyStatusClick(object sender, RoutedEventArgs e)
        => Ui.CopyToClipboard(Vm.StatusMessage);

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await Vm.ForceRefreshAsync();
        SyncHeroPips();
    }

    private void OnMediaClick(object? sender, MediaCard card)
    {
        Frame.Navigate(typeof(MediaDetailsPage), new MediaDetailsArgs(card.Slug, card.MediaType));
    }

    public static bool TryParseMediaLink(string? link, out string slug, out string type)
    {
        slug = "";
        type = "";
        if (string.IsNullOrWhiteSpace(link))
        {
            return false;
        }
        var path = link;
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }
        var kind = parts[0].ToLowerInvariant();
        if (kind is not ("anime" or "manga" or "cinema" or "games" or "books"))
        {
            return false;
        }
        type = kind;
        slug = parts[1];
        return true;
    }
}
