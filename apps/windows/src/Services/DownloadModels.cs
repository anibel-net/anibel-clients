using CommunityToolkit.Mvvm.ComponentModel;

namespace Anibel.App.Services;

public enum DownloadKind
{
    Video,
    Audio,
    Manga,
    File,
}

public enum DownloadStatus
{
    Queued,
    Downloading,
    Completed,
    Failed,
    Cancelled,
}

public enum VideoDownloadFormat { Source, Mkv }

public sealed class EpisodeDownloadRequest
{
    public VideoDownloadFormat VideoFormat { get; init; } = VideoDownloadFormat.Mkv;
    public required string EpisodeId
    {
        get; init;
    }
    public required string EpisodeUrl
    {
        get; init;
    }
    public required string MediaId
    {
        get; init;
    }
    public required string MediaType
    {
        get; init;
    }
    public required string Slug
    {
        get; init;
    }
    public required string Title
    {
        get; init;
    }
    public string? PosterUrl
    {
        get; init;
    }
    public string EpisodeLabel { get; init; } = "";
    public string? EpisodeType
    {
        get; init;
    }
    public bool AudioOnly
    {
        get; init;
    }
}

public sealed class ChapterDownloadRequest
{
    public required string MediaId
    {
        get; init;
    }
    public required string MediaType
    {
        get; init;
    }
    public required string Slug
    {
        get; init;
    }
    public required string Title
    {
        get; init;
    }
    public required double Chapter
    {
        get; init;
    }
    public string? ChapterId
    {
        get; init;
    }
    public string? ChapterTitle
    {
        get; init;
    }
    public string? PosterUrl
    {
        get; init;
    }
    public double[] ChapterList { get; init; } = [];
}

public sealed class FileDownloadRequest
{
    public required string MediaId
    {
        get; init;
    }
    public required string MediaType
    {
        get; init;
    }
    public required string Slug
    {
        get; init;
    }
    public required string Title
    {
        get; init;
    }
    public required string FileUrl
    {
        get; init;
    }
    public string? PosterUrl
    {
        get; init;
    }
    public string? Subtitle
    {
        get; init;
    }
}

/// <summary>Display projection of a Rust-owned download snapshot.</summary>
public sealed partial class DownloadItem : ObservableObject
{
    [ObservableProperty] public partial VideoDownloadFormat VideoFormat { get; set; }
    [ObservableProperty] public partial string Id { get; set; } = "";
    [ObservableProperty] public partial string MediaId { get; set; } = "";
    [ObservableProperty] public partial string MediaType { get; set; } = "";
    [ObservableProperty] public partial string Slug { get; set; } = "";
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty]
    public partial string? PosterUrl
    {
        get; set;
    }
    [ObservableProperty]
    public partial string? PosterPath
    {
        get; set;
    }
    [ObservableProperty]
    public partial string? EpisodeId
    {
        get; set;
    }
    [ObservableProperty]
    public partial string? EpisodeType
    {
        get; set;
    }
    [ObservableProperty]
    public partial string? ChapterId
    {
        get; set;
    }
    [ObservableProperty]
    public partial string? Error
    {
        get; set;
    }
    [ObservableProperty] public partial double[] ChapterList { get; set; } = [];
    [ObservableProperty]
    public partial double? Chapter
    {
        get; set;
    }
    [ObservableProperty]
    public partial double Progress
    {
        get; set;
    }
    [ObservableProperty]
    public partial long BytesReceived
    {
        get; set;
    }
    [ObservableProperty]
    public partial long DiskBytes
    {
        get; set;
    }
    [ObservableProperty]
    public partial int PartsDone
    {
        get; set;
    }
    [ObservableProperty]
    public partial int PartsTotal
    {
        get; set;
    }
    [ObservableProperty]
    public partial bool CanPlay
    {
        get; set;
    }
    [ObservableProperty]
    public partial bool CanSave
    {
        get; set;
    }
    [ObservableProperty]
    public partial bool CanRetry
    {
        get; set;
    }
    [ObservableProperty]
    public partial DownloadKind Kind
    {
        get; set;
    }
    [ObservableProperty]
    public partial DownloadStatus Status
    {
        get; set;
    }

    public bool IsActive => Status is DownloadStatus.Queued or DownloadStatus.Downloading;
    public bool IsCompleted => Status == DownloadStatus.Completed;
    public bool IsFailed => Status == DownloadStatus.Failed;
    public bool HasSlug => !string.IsNullOrWhiteSpace(Slug);
    public bool ProgressIsUnknown => IsActive && PartsTotal <= 0;
    public int ProgressPercent => (int)Math.Clamp(Math.Round(Progress * 100), 0, 100);

    public string KindLabel => Kind switch
    {
        DownloadKind.Video when VideoFormat == VideoDownloadFormat.Mkv => $"{Strings.KindVideo} · MKV",
        DownloadKind.Audio => Strings.KindAudio,
        DownloadKind.Manga => Strings.KindManga,
        DownloadKind.File => Strings.KindFile,
        _ => Strings.KindVideo,
    };

    public string StatusLabel => Status switch
    {
        DownloadStatus.Queued => Strings.StatusQueued,
        DownloadStatus.Downloading => PartsTotal > 0
            ? Strings.StatusDownloadingParts(PartsDone, PartsTotal)
            : Strings.StatusDownloading,
        DownloadStatus.Completed => Strings.StatusCompleted,
        DownloadStatus.Failed => string.IsNullOrWhiteSpace(Error) ? Strings.StatusFailed : Error,
        DownloadStatus.Cancelled => Strings.StatusCancelled,
        _ => "",
    };

    public string ProgressLabel
    {
        get
        {
            if (Status == DownloadStatus.Completed)
            {
                return SizeLabel;
            }
            if (PartsTotal > 0)
            {
                return $"{PartsDone} / {PartsTotal} · {ProgressPercent}%";
            }
            if (BytesReceived > 0)
            {
                return FormatBytes(BytesReceived);
            }
            return StatusLabel;
        }
    }

    public string SizeLabel
    {
        get
        {
            var size = DiskBytes;
            return size > 0 ? FormatBytes(size) : "";
        }
    }

    public string PlayLabel => Kind == DownloadKind.Manga ? Strings.PlayChapter : Strings.PlayEpisode;

    public string DisplayPoster =>
        !string.IsNullOrEmpty(PosterPath)
            ? PosterPath
            : PosterUrl ?? "";


    public static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = [Strings.UnitBytes, Strings.UnitKb, Strings.UnitMb, Strings.UnitGb, Strings.UnitTb];
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.#} {units[index]}";
    }
    private static readonly System.Reflection.PropertyInfo[] SnapshotProperties = typeof(DownloadItem).GetProperties().Where(p => p.CanWrite).ToArray();
    public void Apply(DownloadItem source)
    {
        var changed = false;
        foreach (var property in SnapshotProperties)
        {
            var old = property.GetValue(this);
            var next = property.GetValue(source);
            if (old is Array a && next is Array b && a.Cast<object>().SequenceEqual(b.Cast<object>()))
                continue;
            if (!Equals(old, next))
            {
                property.SetValue(this, next);
                changed = true;
            }
        }
        if (changed)
            OnPropertyChanged(string.Empty);
    }
}
