namespace Anibel.App.Playback;

public readonly record struct AudioTrackInfo(long Id, string Lang, string Title);
public readonly record struct SubtitleTrackInfo(long Id, string Title, string Lang, string FileName);
public sealed record TrackSelectionDto(long? Audio, long? Subtitle);
