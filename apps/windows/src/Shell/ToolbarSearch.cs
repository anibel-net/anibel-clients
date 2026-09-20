using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.Views.Converters;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;

namespace Anibel.App;

internal sealed class ToolbarSearch : IDisposable
{
    private readonly AutoSuggestBox GlobalSearch;
    private readonly Func<UIElement?> _focusTarget;
    private readonly ICoreClient _core;
    private readonly SearchHistory _searchHistory;
    private readonly DispatcherQueueTimer _suggestTimer;
    private DispatcherQueue DispatcherQueue => GlobalSearch.DispatcherQueue;
    private CancellationTokenSource _suggestCts = new();
    private string _suggestQuery = "";
    private bool _removingHistory;
    private bool _focusSearchRequested;
    public ToolbarSearch(AutoSuggestBox box, ICoreClient core, SearchHistory history, Func<UIElement?> focusTarget)
    {
        GlobalSearch = box; _core = core; _searchHistory = history; _focusTarget = focusTarget;
        _suggestTimer = box.DispatcherQueue.CreateTimer();
        _suggestTimer.Interval = TimeSpan.FromMilliseconds(280);
        _suggestTimer.IsRepeating = false;
        _suggestTimer.Tick += OnTimer;
    }
    private void OnTimer(DispatcherQueueTimer sender, object args) => _ = SuggestAsync();
    public void Focus()
    {
        _focusSearchRequested = true;
        try { GlobalSearch.Focus(FocusState.Programmatic); }
        finally { _focusSearchRequested = false; }
    }
    public void Dispose()
    {
        _suggestTimer.Stop(); _suggestTimer.Tick -= OnTimer;
        _suggestCts.Cancel(); _suggestCts.Dispose();
    }
    public void OnSearchPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _focusSearchRequested = true;
        try { GlobalSearch.Focus(FocusState.Pointer); }
        finally { _focusSearchRequested = false; }
    }

    public void OnSearchGettingFocus(UIElement sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs e)
    {
        // A closing player's controls can still be loaded when WinUI moves focus.
        // Only explicit search input, keyboard traversal, or focus within search is allowed.
        if (_focusSearchRequested || KeyboardNavigation.FocusIsWithin(GlobalSearch)
            || (e.FocusState == FocusState.Keyboard && e.Direction is
                Microsoft.UI.Xaml.Input.FocusNavigationDirection.Next or Microsoft.UI.Xaml.Input.FocusNavigationDirection.Previous)) return;
        if (!e.TryCancel() && _focusTarget() is { } target)
            e.TrySetNewFocusedElement(target);
    }

    public void OnSearchGotFocus(object sender, RoutedEventArgs e)
    {
        if (!KeyboardNavigation.FocusIsWithin(GlobalSearch)) return;
        ShowLocalSuggestions(GlobalSearch.Text);
        var q = GlobalSearch.Text?.Trim() ?? "";
        if (q.Length >= 2)
        {
            _suggestQuery = GlobalSearch.Text ?? q;
            _ = SuggestAsync();
        }
    }

    public void OnSearchLostFocus(object sender, RoutedEventArgs e)
    {
        if (KeyboardNavigation.FocusIsWithin(GlobalSearch)) return;
        CloseSearchSuggestions();
    }

    private void CloseSearchSuggestions()
    {
        _suggestTimer.Stop();
        _suggestCts.Cancel();
        GlobalSearch.IsSuggestionListOpen = false;
    }

    public void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }
        _suggestCts.Cancel();
        _suggestQuery = sender.Text ?? "";
        var q = _suggestQuery.Trim();
        ShowLocalSuggestions(_suggestQuery);
        _suggestTimer.Stop();
        if (q.Length >= 2)
        {
            _suggestTimer.Start();
        }
    }

    private void ShowLocalSuggestions(string? text)
    {
        if (!KeyboardNavigation.FocusIsWithin(GlobalSearch)) return;
        var q = (text ?? "").Trim();
        var items = QuickSearch.Build(q, [], _searchHistory.Items);
        GlobalSearch.ItemsSource = items;
        GlobalSearch.IsSuggestionListOpen = items.Count > 0;
    }

    private async Task SuggestAsync()
    {
        var q = _suggestQuery.Trim();
        if (q.Length < 2 || !KeyboardNavigation.FocusIsWithin(GlobalSearch))
        {
            return;
        }

        try
        {
            _suggestCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        _suggestCts.Dispose();
        _suggestCts = new CancellationTokenSource();
        var ct = _suggestCts.Token;

        try
        {
            var core = _core;
            var hits = await core.SearchAsync(q, QuickSearch.FetchCount, ct);
            if (ct.IsCancellationRequested || q != (GlobalSearch.Text ?? "").Trim()
                || !KeyboardNavigation.FocusIsWithin(GlobalSearch))
            {
                return;
            }
            GlobalSearch.ItemsSource = QuickSearch.Build(q, hits, _searchHistory.Items);
            GlobalSearch.IsSuggestionListOpen = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Diag.Log($"quick search: {ex.Message}");
        }
    }

    public void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (_removingHistory)
        {
            return;
        }
        if (args.SelectedItem is SearchSuggestion { IsMore: true })
        {
            sender.Text = _suggestQuery;
        }
        else if (args.SelectedItem is SearchSuggestion { IsHistory: true, Title: { Length: > 0 } history })
        {
            sender.Text = history;
        }
        else if (args.SelectedItem is SearchSuggestion { Media: { } media })
        {
            sender.Text = CardDisplay.Title(media);
        }
    }

    public void OnGlobalSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_removingHistory)
        {
            _removingHistory = false;
            return;
        }
        if (args.ChosenSuggestion is SearchSuggestion chosen)
        {
            CloseSearchSuggestions();
            if (chosen.IsMore)
            {
                OpenFullSearch(chosen.Subtitle.Length > 0 ? chosen.Subtitle : sender.Text);
            }
            else if (chosen.IsHistory)
            {
                OpenFullSearch(chosen.Title);
            }
            else if (chosen.Media is { Slug: { Length: > 0 } slug } media)
            {
                Remember(sender.Text);
                WeakReferenceMessenger.Default.Send(new OpenMediaMessage(slug, media.MediaType));
            }
            return;
        }

        OpenFullSearch(args.QueryText);
    }

    public async void OnRemoveHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string query } || query.Length == 0)
        {
            return;
        }
        _removingHistory = true;
        try { await _searchHistory.Remove(query); }
        catch (Exception ex) { Diag.Log($"search history: {ex.Message}"); }
        ShowLocalSuggestions(GlobalSearch.Text);
        if (GlobalSearch.Text?.Trim().Length >= 2)
        {
            _ = SuggestAsync();
        }
        DispatcherQueue.TryEnqueue(() => _removingHistory = false);
    }

    public void OpenFullSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        Remember(text);
        CloseSearchSuggestions();
        WeakReferenceMessenger.Default.Send(new GlobalSearchMessage(text.Trim()));
    }

    private async void Remember(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { await _searchHistory.Add(text); } catch (Exception ex) { Diag.Log($"search history: {ex.Message}"); }
        }
    }

}
