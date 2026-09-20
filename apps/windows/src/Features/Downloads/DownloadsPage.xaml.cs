using System.Diagnostics;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Anibel.App.Views;

public sealed partial class DownloadsPage : Page
{
    public DownloadsViewModel Vm { get; }

    public DownloadsPage()
    {
        Vm = App.Services.GetRequiredService<DownloadsViewModel>();
        InitializeComponent();
        Loaded += (_, _) => Vm.Refresh();
    }

    private void OnCopyErrorClick(object sender, RoutedEventArgs e)
    {
        var text = (sender as FrameworkElement)?.Tag as string;
        if (string.IsNullOrEmpty(text) && sender is FrameworkElement fe && fe.DataContext is DownloadItem item)
        {
            text = item.Error;
        }
        Ui.CopyToClipboard(text);
    }

    private void OnFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag)
        {
            Vm.Filter = tag;
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadItem item } || !item.CanPlay)
        {
            return;
        }
        if (item.Kind == DownloadKind.Manga)
        {
            var chapters = item.ChapterList is { Length: > 0 }
                ? item.ChapterList
                : item.Chapter is { } n ? new[] { n } : [];
            WeakReferenceMessenger.Default.Send(new ReadChapterMessage(new ReaderArgs(
                item.Slug,
                item.Chapter ?? 0,
                item.Title,
                item.Subtitle,
                item.ChapterId,
                chapters)));
            return;
        }

        WeakReferenceMessenger.Default.Send(new PlayEpisodeMessage(new PlayerArgs(
            item.Slug, item.MediaType, "", item.Title, item.Subtitle, item.EpisodeId,
            DownloadId: item.Id, EpisodeType: item.EpisodeType)));
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadItem item } || !item.CanSave)
        {
            return;
        }
        try
        {
            var picker = new FolderPicker();
            InitPicker(picker);
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) await Vm.CopyToFolderAsync(item, folder.Path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadItem item })
        {
            try { await Vm.Cancel(item); } catch (Exception ex) { await ShowErrorAsync(ex); }
        }
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadItem item })
        {
            try { await Vm.Retry(item); } catch (Exception ex) { await ShowErrorAsync(ex); }
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadItem item })
        {
            try { await Vm.Delete(item); } catch (Exception ex) { await ShowErrorAsync(ex); }
        }
    }

    private void OnOpenTitleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadItem item } && !string.IsNullOrWhiteSpace(item.Slug))
        {
            WeakReferenceMessenger.Default.Send(new OpenMediaMessage(item.Slug, item.MediaType));
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Vm.Root}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Diag.Log($"open downloads folder: {ex.Message}");
        }
    }

    private static void InitPicker(object picker)
    {
        if (App.CurrentWindow is null)
        {
            return;
        }
        var hwnd = WindowNative.GetWindowHandle(App.CurrentWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private async Task ShowErrorAsync(Exception ex)
    {
        var dialog = new ContentDialog { Title = Strings.Sorry, Content = Ui.DisplayMessage(ex), CloseButtonText = Strings.Ok, XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }
}
