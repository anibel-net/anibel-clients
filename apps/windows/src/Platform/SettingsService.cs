using System.Text.Json;

namespace Anibel.App.Services;

public enum WindowBackground { Solid, Mica, Acrylic }

/// <summary>
/// App-level settings (language, API endpoints) — plain JSON in LocalAppData.
/// Endpoints are passed to the Rust core at init (dev override support).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public string Language { get; set; } = "be";
    public WindowBackground Background { get; set; } = WindowBackground.Mica;
    public string ApiBaseUrl { get; set; } = "https://anibel.net/graphql";
    public string VideoBaseUrl { get; set; } = "https://api.anibel.stream";

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Anibel");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }
        try
        {
            var dto = JsonSerializer.Deserialize<SettingsService>(File.ReadAllText(_path), Json);
            if (dto is not null)
            {
                Language = string.IsNullOrWhiteSpace(dto.Language) ? Language : dto.Language;
                Background = Enum.IsDefined(dto.Background) ? dto.Background : WindowBackground.Mica;
                ApiBaseUrl = string.IsNullOrWhiteSpace(dto.ApiBaseUrl) ? ApiBaseUrl : dto.ApiBaseUrl;
                VideoBaseUrl = string.IsNullOrWhiteSpace(dto.VideoBaseUrl) ? VideoBaseUrl : dto.VideoBaseUrl;
            }
        }
        catch (JsonException)
        {
            // corrupted — keep defaults
        }
    }

    public void Save() =>
        File.WriteAllText(_path, JsonSerializer.Serialize(this, Json));
}
