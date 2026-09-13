namespace Anibel.App.Views;

/// <summary>Navigation argument: episode URL (+ display context).</summary>
public sealed record PlayerArgs(
    string Slug,
    string MediaType,
    string Url,
    string TitleLabel,
    string EpisodeLabel,
    string? EpisodeId,
    string? DownloadId = null,
    string? EpisodeType = null);
