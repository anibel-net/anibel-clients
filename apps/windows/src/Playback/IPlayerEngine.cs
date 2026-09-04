using Anibel.App.Core;

namespace Anibel.App.Playback;

/// <summary>
/// Host playback engine. Core only resolves a <see cref="PlaybackIntentDto"/>;
/// the engine owns native handles and host surfaces. Implementations that also
/// expose transport/audio/subtitle controls implement <see cref="IPlaybackControls"/>
/// as well (e.g. <see cref="MpvEngine"/>) — the embed engine deliberately does not.
/// </summary>
public interface IPlayerEngine : IDisposable
{
    event Action? Ready;
    event Action? Ended;
    event Action<string>? Error;
    event Action<double, double>? PositionChanged;
    event Action<bool>? PauseChanged;

    bool IsInitialized { get; }
    bool HasSubs { get; }

    void Load(PlaybackIntentDto intent, IReadOnlyList<string> subPaths);
    Task LoadAsync(PlaybackIntentDto intent, IReadOnlyList<string> subPaths, CancellationToken ct = default);
}

/// <summary>
/// Transport/audio/subtitle controls of a native playback engine. The embed
/// pipeline (WebView2) has no native transport, so it implements only
/// <see cref="IPlayerEngine"/>; consumers must null-check
/// <see cref="PlayerController.Controls"/>.
/// </summary>
public interface IPlaybackControls
{
    double Position { get; }
    double Duration { get; }
    double Volume { get; }
    bool IsMuted { get; }
    long SubTrack { get; }

    void TogglePause();
    void Seek(double seconds);
    void SetVolume(double volume);
    void SetMute(bool mute);
    void SetSubTrack(long id);
    void Resize(uint width, uint height);
    void InvalidateSurface();
}
