namespace Anibel.App.Playback;

/// <summary>
/// Per-episode playback resume positions (seconds). Injectable so tests never
/// touch the real LocalAppData-backed <see cref="Services.ResumeStore"/>.
/// </summary>
public interface IResumeStore
{
    double Get(string episodeId);
    void Set(string episodeId, double positionSecs);
    void Clear(string episodeId);
}

/// <summary>
/// Production adapter over the static <see cref="Services.ResumeStore"/>
/// (%LocalAppData%\Anibel\resume.json). Wired when no store is injected.
/// </summary>
internal sealed class ResumeStoreAdapter : IResumeStore
{
    public static readonly ResumeStoreAdapter Instance = new();

    public double Get(string episodeId) => Services.ResumeStore.Get(episodeId);
    public void Set(string episodeId, double positionSecs) => Services.ResumeStore.Set(episodeId, positionSecs);
    public void Clear(string episodeId) => Services.ResumeStore.Clear(episodeId);
}
