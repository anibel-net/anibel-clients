namespace Anibel.App.Playback;

public sealed record VideoTrackInfo(long Id, long Width, long Height, long Bitrate, string Codec, bool Image, bool Selected);
public sealed record VideoQualityChoice(long Id, string Label, bool Selected);
public sealed record VideoQualitiesDto(VideoQualityChoice[] Choices, long? Video);
