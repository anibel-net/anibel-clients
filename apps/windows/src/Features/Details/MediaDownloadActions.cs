using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using Anibel.App.Views.Converters;
namespace Anibel.App.Views;

/// <summary>Title download actions. The page owns status presentation, not request construction.</summary>
internal sealed class MediaDownloadActions(MediaDetailsViewModel Vm, DownloadService Downloads,
    Action<string> ShowDownloadNote, Action<Exception> ShowDownloadError)
{
    public async Task<bool> QueueEpisode(EpisodeDto ep, bool audioOnly, bool silent = false)
    {
        if (Vm.Media is null || string.IsNullOrWhiteSpace(ep.Url) || string.IsNullOrWhiteSpace(ep.Id))
        {
            return false;
        }
        try
        {
            await Downloads.EnqueueEpisode(new EpisodeDownloadRequest
            {
                EpisodeId = ep.Id,
                EpisodeUrl = ep.Url,
                MediaId = Vm.Media.MediaId,
                MediaType = Vm.Media.MediaType,
                Slug = Vm.Media.Slug,
                Title = Vm.DisplayTitle,
                PosterUrl = Vm.PosterUrl,
                EpisodeLabel = EpisodeDisplay.Number(ep),
                EpisodeType = ep.Type,
                AudioOnly = audioOnly,
            });
            if (!silent)
            {
                ShowDownloadNote(audioOnly
                    ? Strings.AudioAddedToDownloads
                    : Strings.EpisodeAddedToDownloads);
            }
            return true;
        }
        catch (Exception ex) { ShowDownloadError(ex); return false; }
    }

    public async Task<bool> QueueChapter(ChapterDto ch, bool silent = false)
    {
        if (Vm.Media is null)
        {
            return false;
        }
        try
        {
            await Downloads.EnqueueChapter(new ChapterDownloadRequest
            {
                MediaId = Vm.Media.MediaId,
                MediaType = Vm.Media.MediaType,
                Slug = Vm.Media.Slug,
                Title = Vm.DisplayTitle,
                Chapter = ch.Chapter,
                ChapterId = ch.Id,
                ChapterTitle = ChapterDisplay.TitleLabel(ch),
                PosterUrl = Vm.PosterUrl,
                ChapterList = Vm.Chapters.Select(c => c.Chapter).ToArray(),
            });
            if (!silent)
            {
                ShowDownloadNote(Strings.ChapterAddedToDownloads);
            }
            return true;
        }
        catch (Exception ex) { ShowDownloadError(ex); return false; }
    }

    public async Task QueueAllEpisodes()
    {
        var n = 0;
        foreach (var ep in Vm.Episodes.ToArray())
        {
            if (await QueueEpisode(ep, audioOnly: false, silent: true))
            {
                n++;
            }
        }
        ShowDownloadNote(n == 0
            ? Strings.NoEpisodesToDownload
            : Strings.QueueAdded(n));
    }

    public async Task QueueAllChapters()
    {
        var n = 0;
        foreach (var ch in Vm.Chapters.ToArray())
        {
            if (await QueueChapter(ch, silent: true))
            {
                n++;
            }
        }
        ShowDownloadNote(n == 0
            ? Strings.NoChaptersToDownload
            : Strings.QueueAdded(n));
    }

    public async Task QueueFile()
    {
        if (Vm.Media is null || string.IsNullOrWhiteSpace(Vm.DownloadUrl))
        {
            return;
        }
        try
        {
            await Downloads.EnqueueFile(new FileDownloadRequest
            {
                MediaId = Vm.Media.MediaId,
                MediaType = Vm.Media.MediaType,
                Slug = Vm.Media.Slug,
                Title = Vm.DisplayTitle,
                FileUrl = Vm.DownloadUrl,
                PosterUrl = Vm.PosterUrl,
                Subtitle = Strings.KindFile,
            });
            ShowDownloadNote(Strings.FileAddedToDownloads);
        }
        catch (Exception ex) { ShowDownloadError(ex); }
    }

}
