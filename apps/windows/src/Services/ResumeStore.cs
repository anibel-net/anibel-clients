using System.Text.Json;

namespace Anibel.App.Services;

/// <summary>
/// Playback resume positions: { episodeId → positionSecs } in
/// %LocalAppData%\Anibel\resume.json. Cleared when an episode finishes.
/// </summary>
public static class ResumeStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly object Lock = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Anibel", "resume.json");

    private static Dictionary<string, double> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(FilePath), Json)
                       ?? new Dictionary<string, double>();
            }
        }
        catch (JsonException)
        {
        }
        return new Dictionary<string, double>();
    }

    private static void Save(Dictionary<string, double> map)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(map, Json));
        }
        catch (IOException)
        {
        }
    }

    public static double Get(string episodeId)
    {
        if (string.IsNullOrEmpty(episodeId))
        {
            return 0;
        }
        lock (Lock)
        {
            return Load().TryGetValue(episodeId, out var pos) ? pos : 0;
        }
    }

    public static void Set(string episodeId, double positionSecs)
    {
        if (string.IsNullOrEmpty(episodeId))
        {
            return;
        }
        lock (Lock)
        {
            var map = Load();
            map[episodeId] = positionSecs;
            Save(map);
        }
    }

    public static void Clear(string episodeId)
    {
        if (string.IsNullOrEmpty(episodeId))
        {
            return;
        }
        lock (Lock)
        {
            var map = Load();
            if (map.Remove(episodeId))
            {
                Save(map);
            }
        }
    }
}
