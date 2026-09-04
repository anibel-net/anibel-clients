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

public sealed class EpisodeDownloadRequest
{
    public required string EpisodeId { get; init; }
    public required string EpisodeUrl { get; init; }
    public required string MediaId { get; init; }
    public required string MediaType { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? PosterUrl { get; init; }
    public string EpisodeLabel { get; init; } = "";
    public string? EpisodeType { get; init; }
    public bool AudioOnly { get; init; }
}

public sealed class ChapterDownloadRequest
{
    public required string MediaId { get; init; }
    public required string MediaType { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public required double Chapter { get; init; }
    public string? ChapterId { get; init; }
    public string? ChapterTitle { get; init; }
    public string? PosterUrl { get; init; }
    public double[] ChapterList { get; init; } = [];
}

public sealed class FileDownloadRequest
{
    public required string MediaId { get; init; }
    public required string MediaType { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public required string FileUrl { get; init; }
    public string? PosterUrl { get; init; }
    public string? Subtitle { get; init; }
}

/// <summary>One library entry — persisted under LocalAppData\Anibel\downloads.</summary>
public sealed partial class DownloadItem : ObservableObject
{
    public string Id { get; set; } = "";
    public DownloadKind Kind { get; set; }
    public string MediaId { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? PosterUrl { get; set; }
    [ObservableProperty] private string? posterPath;
    public string? EpisodeUrl { get; set; }
    public string? EpisodeId { get; set; }
    public string? EpisodeType { get; set; }
    public string? ChapterId { get; set; }
    public double? Chapter { get; set; }
    public double[] ChapterList { get; set; } = [];
    public string? FileUrl { get; set; }
    public string Folder { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [ObservableProperty] private DownloadStatus status = DownloadStatus.Queued;
    [ObservableProperty] private double progress;
    [ObservableProperty] private long bytesReceived;
    [ObservableProperty] private long? bytesTotal;
    [ObservableProperty] private int partsDone;
    [ObservableProperty] private int partsTotal;
    [ObservableProperty] private string? error;
    [ObservableProperty] private string? videoPath;
    [ObservableProperty] private string? audioPath;
    [ObservableProperty] private string[] subtitlePaths = [];
    [ObservableProperty] private string[] fontPaths = [];
    [ObservableProperty] private string[] imagePaths = [];
    [ObservableProperty] private string? filePath;
    [ObservableProperty] private DateTimeOffset? completedAt;

    public bool IsActive => Status is DownloadStatus.Queued or DownloadStatus.Downloading;
    public bool IsCompleted => Status == DownloadStatus.Completed;
    public bool IsFailed => Status == DownloadStatus.Failed;
    public bool CanRetry => Status is DownloadStatus.Failed or DownloadStatus.Cancelled;
    public bool CanPlay => IsCompleted && (HasVideo || HasAudio || HasImages);
    public bool CanSave => IsCompleted && HasAnyFile;
    public bool HasSlug => !string.IsNullOrWhiteSpace(Slug);
    public bool ProgressIsUnknown => IsActive && PartsTotal <= 0 && BytesTotal is not > 0;
    public bool HasVideo => !string.IsNullOrEmpty(VideoPath) && File.Exists(VideoPath);
    public bool HasAudio => !string.IsNullOrEmpty(AudioPath) && File.Exists(AudioPath);
    public bool HasImages => ImagePaths.Any(File.Exists);
    public bool HasAnyFile =>
        HasVideo || HasAudio || HasImages
        || (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath));

    public int ProgressPercent => (int)Math.Clamp(Math.Round(Progress * 100), 0, 100);

    public string KindLabel => Kind switch
    {
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
            if (BytesTotal is > 0)
            {
                return $"{FormatBytes(BytesReceived)} / {FormatBytes(BytesTotal.Value)} · {ProgressPercent}%";
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
            var size = OnDiskBytes();
            return size > 0 ? FormatBytes(size) : "";
        }
    }

    public string PlayLabel => Kind == DownloadKind.Manga ? Strings.PlayChapter : Strings.PlayEpisode;

    public string DisplayPoster =>
        !string.IsNullOrEmpty(PosterPath) && File.Exists(PosterPath)
            ? PosterPath
            : PosterUrl ?? "";

    private long? _onDiskBytes;

    public long OnDiskBytes()
    {
        if (_onDiskBytes is { } cached)
        {
            return cached;
        }
        long n = 0;
        n += Len(VideoPath);
        n += Len(AudioPath);
        n += Len(FilePath);
        n += Len(PosterPath);
        foreach (var p in SubtitlePaths) n += Len(p);
        foreach (var p in FontPaths) n += Len(p);
        foreach (var p in ImagePaths) n += Len(p);
        _onDiskBytes = n;
        return n;
    }

    /// <summary>Drops the cached disk size; call whenever a counted path changes.</summary>
    private void InvalidateOnDiskBytes() => _onDiskBytes = null;

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} {Strings.UnitBytes}";
        double v = bytes;
        string[] units = [Strings.UnitKb, Strings.UnitMb, Strings.UnitGb, Strings.UnitTb];
        foreach (var unit in units)
        {
            v /= 1024;
            if (v < 1024)
            {
                return v < 10 ? $"{v:0.0} {unit}" : $"{v:0} {unit}";
            }
        }
        return $"{v:0.0} {Strings.UnitTb}";
    }

    private static long Len(string? path) =>
        !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0;

    partial void OnStatusChanged(DownloadStatus value) { InvalidateOnDiskBytes(); RaiseUi(); }
    partial void OnProgressChanged(double value) => RaiseUi();
    partial void OnBytesReceivedChanged(long value) => RaiseUi();
    partial void OnBytesTotalChanged(long? value) => RaiseUi();
    partial void OnPartsDoneChanged(int value) => RaiseUi();
    partial void OnPartsTotalChanged(int value) => RaiseUi();
    partial void OnErrorChanged(string? value) => RaiseUi();
    partial void OnPosterPathChanged(string? value) { InvalidateOnDiskBytes(); RaiseUi(); }
    partial void OnVideoPathChanged(string? value) { InvalidateOnDiskBytes(); RaiseUi(); }
    partial void OnAudioPathChanged(string? value) { InvalidateOnDiskBytes(); RaiseUi(); }
    partial void OnSubtitlePathsChanged(string[] value) { InvalidateOnDiskBytes(); RaiseUi(); }
    partial void OnFontPathsChanged(string[] value) { InvalidateOnDiskBytes(); RaiseUi(); }
    partial void OnImagePathsChanged(string[] value) { InvalidateOnDiskBytes(); RaiseUi(); }
    partial void OnFilePathChanged(string? value) { InvalidateOnDiskBytes(); RaiseUi(); }

    private void RaiseUi()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ProgressIsUnknown));
        OnPropertyChanged(nameof(HasSlug));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(HasAudio));
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(HasAnyFile));
    }

    public static string EpisodeKey(string episodeId, bool audioOnly) =>
        audioOnly ? $"aud:{episodeId}" : $"ep:{episodeId}";

    public static string ChapterKey(string slug, double chapter) =>
        $"ch:{slug}:{chapter:0.##}";

    public static string FileKey(string mediaId) => $"file:{mediaId}";
}
