using System.Text.Json;

namespace Anibel.App.Services;

/// <summary>
/// Persists the offline download library as JSON (library.json) and maps items
/// to/from snapshots. The queue owner writes items, the store owns I/O.
/// </summary>
public sealed class DownloadStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _libraryPath;

    public DownloadStore(string libraryPath) => _libraryPath = libraryPath;

    /// <summary>Reads the library from disk; returns an empty list if missing or corrupt.</summary>
    public List<DownloadItem> Load()
    {
        if (!File.Exists(_libraryPath))
        {
            return [];
        }
        try
        {
            var snaps = JsonSerializer.Deserialize<List<DownloadSnapshot>>(
                File.ReadAllText(_libraryPath), Json);
            if (snaps is null)
            {
                return [];
            }
            var items = new List<DownloadItem>(snaps.Count);
            foreach (var snap in snaps)
            {
                var item = snap.ToItem();
                if (item.Status is DownloadStatus.Downloading or DownloadStatus.Queued)
                {
                    item.Status = DownloadStatus.Queued;
                    item.Progress = 0;
                }
                if (item.Status == DownloadStatus.Completed && !item.HasAnyFile && !item.CanPlay)
                {
                    item.Status = DownloadStatus.Failed;
                    item.Error = "Файлы зніклі з дыска.";
                }
                items.Add(item);
            }
            return items;
        }
        catch (Exception ex)
        {
            Diag.Log($"download library load: {ex.Message}");
            return [];
        }
    }

    /// <summary>Serializes the given items to disk; failures are logged, never thrown.</summary>
    public void Save(IEnumerable<DownloadItem> items)
    {
        try
        {
            var snaps = items.Select(DownloadSnapshot.From).ToList();
            File.WriteAllText(_libraryPath, JsonSerializer.Serialize(snaps, Json));
        }
        catch (Exception ex)
        {
            Diag.Log($"download library save: {ex.Message}");
        }
    }
}

/// <summary>Serializable projection of a <see cref="DownloadItem"/>.</summary>
internal sealed class DownloadSnapshot
{
    public string Id { get; set; } = "";
    public DownloadKind Kind { get; set; }
    public DownloadStatus Status { get; set; }
    public string MediaId { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? PosterPath { get; set; }
    public string? EpisodeUrl { get; set; }
    public string? EpisodeId { get; set; }
    public string? EpisodeType { get; set; }
    public string? ChapterId { get; set; }
    public double? Chapter { get; set; }
    public double[] ChapterList { get; set; } = [];
    public string? FileUrl { get; set; }
    public string Folder { get; set; } = "";
    public string? VideoPath { get; set; }
    public string? AudioPath { get; set; }
    public string[] SubtitlePaths { get; set; } = [];
    public string[] FontPaths { get; set; } = [];
    public string[] ImagePaths { get; set; } = [];
    public string? FilePath { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static DownloadSnapshot From(DownloadItem i) => new()
    {
        Id = i.Id,
        Kind = i.Kind,
        Status = i.Status is DownloadStatus.Downloading ? DownloadStatus.Queued : i.Status,
        MediaId = i.MediaId,
        MediaType = i.MediaType,
        Slug = i.Slug,
        Title = i.Title,
        Subtitle = i.Subtitle,
        PosterUrl = i.PosterUrl,
        PosterPath = i.PosterPath,
        EpisodeUrl = i.EpisodeUrl,
        EpisodeId = i.EpisodeId,
        EpisodeType = i.EpisodeType,
        ChapterId = i.ChapterId,
        Chapter = i.Chapter,
        ChapterList = i.ChapterList,
        FileUrl = i.FileUrl,
        Folder = i.Folder,
        VideoPath = i.VideoPath,
        AudioPath = i.AudioPath,
        SubtitlePaths = i.SubtitlePaths,
        FontPaths = i.FontPaths,
        ImagePaths = i.ImagePaths,
        FilePath = i.FilePath,
        Error = i.Error,
        CreatedAt = i.CreatedAt,
        CompletedAt = i.CompletedAt,
    };

    public DownloadItem ToItem() => new()
    {
        Id = Id,
        Kind = Kind,
        Status = Status,
        MediaId = MediaId,
        MediaType = MediaType,
        Slug = Slug,
        Title = Title,
        Subtitle = Subtitle,
        PosterUrl = PosterUrl,
        PosterPath = PosterPath,
        EpisodeUrl = EpisodeUrl,
        EpisodeId = EpisodeId,
        EpisodeType = EpisodeType,
        ChapterId = ChapterId,
        Chapter = Chapter,
        ChapterList = ChapterList,
        FileUrl = FileUrl,
        Folder = Folder,
        VideoPath = VideoPath,
        AudioPath = AudioPath,
        SubtitlePaths = SubtitlePaths,
        FontPaths = FontPaths,
        ImagePaths = ImagePaths,
        FilePath = FilePath,
        Error = Error,
        CreatedAt = CreatedAt,
        CompletedAt = CompletedAt,
        Progress = Status == DownloadStatus.Completed ? 1 : 0,
    };
}
