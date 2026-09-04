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
                chapters,
                item.ImagePaths)));
            return;
        }

        var media = item.HasVideo ? item.VideoPath : item.AudioPath;
        WeakReferenceMessenger.Default.Send(new PlayEpisodeMessage(new PlayerArgs(
            item.EpisodeUrl ?? "",
            item.Title,
            item.Subtitle,
            item.EpisodeId,
            media,
            item.SubtitlePaths,
            item.FontPaths,
            item.EpisodeType)));
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadItem item } || !item.CanSave)
        {
            return;
        }
        try
        {
            if (item.Kind == DownloadKind.Manga)
            {
                var picker = new FolderPicker();
                InitPicker(picker);
                picker.SuggestedStartLocation = PickerLocationId.Downloads;
                picker.FileTypeFilter.Add("*");
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null)
                {
                    return;
                }
                await Vm.CopyToFolderAsync(item, folder.Path);
                return;
            }

            var src = item.Kind == DownloadKind.Audio
                ? item.AudioPath ?? item.VideoPath ?? item.FilePath
                : item.VideoPath ?? item.AudioPath ?? item.FilePath;
            if (src is null || !File.Exists(src))
            {
                return;
            }
            var pickerFile = new FileSavePicker();
            InitPicker(pickerFile);
            pickerFile.SuggestedStartLocation = PickerLocationId.Downloads;
            var ext = Path.GetExtension(src);
            if (string.IsNullOrEmpty(ext))
            {
                ext = ".bin";
            }
            pickerFile.FileTypeChoices.Add(Strings.MediaFileType, [ext]);
            pickerFile.SuggestedFileName = SanitizeFile($"{item.Title} - {item.Subtitle}");
            var file = await pickerFile.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }
            File.Copy(src, file.Path, overwrite: true);
            var destDir = Path.GetDirectoryName(file.Path);
            if (destDir is not null)
            {
                var stem = Path.GetFileNameWithoutExtension(file.Path);
                foreach (var sub in item.SubtitlePaths.Where(File.Exists))
                {
                    File.Copy(sub, Path.Combine(destDir, stem + Path.GetExtension(sub)), overwrite: true);
                }
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"save download: {ex.Message}");
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadItem item })
        {
            Vm.Cancel(item);
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadItem item })
        {
            Vm.Retry(item);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadItem item })
        {
            Vm.Delete(item);
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
            Directory.CreateDirectory(Vm.Root);
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

    private static string SanitizeFile(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        name = name.Trim().Trim('.');
        return name.Length == 0 ? "anibel" : name.Length > 80 ? name[..80] : name;
    }
}
