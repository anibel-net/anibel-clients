namespace Anibel.App.Views;

/// <summary>Navigation argument: episode URL (+ display context).</summary>
public sealed record PlayerArgs(
    string Url,
    string TitleLabel,
    string EpisodeLabel,
    string? EpisodeId,
    string? LocalVideoPath = null,
    IReadOnlyList<string>? LocalSubPaths = null,
    IReadOnlyList<string>? LocalFontPaths = null,
    string? EpisodeType = null);
