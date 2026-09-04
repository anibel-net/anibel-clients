using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Anibel.App.Services;

/// <summary>
/// Session persistence: JWT token sealed with DPAPI (token.bin), cached
/// profile in %LocalAppData%\Anibel\session.json. The plaintext token is
/// NEVER written to session.json — that would defeat DPAPI entirely.
/// Core itself never persists tokens.
/// </summary>
public sealed class SessionService
{
    private record Session(string? Username, string? UserId, string? Avatar);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _sessionPath;
    private readonly string _tokenPath;
    private string? _token;
    private bool _tokenLoaded;

    public SessionService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Anibel");
        Directory.CreateDirectory(dir);
        _sessionPath = Path.Combine(dir, "session.json");
        _tokenPath = Path.Combine(dir, "token.bin");
        SanitizeLegacySessionFile();
    }

    /// <summary>Older builds stored the raw token in session.json — strip it.</summary>
    private void SanitizeLegacySessionFile()
    {
        try
        {
            if (!File.Exists(_sessionPath))
                return;
            using var doc = JsonDocument.Parse(File.ReadAllText(_sessionPath));
            if (!doc.RootElement.TryGetProperty("token", out _))
                return;
            var session = JsonSerializer.Deserialize<Session>(doc.RootElement.GetRawText(), Json);
            File.WriteAllText(_sessionPath, JsonSerializer.Serialize(session, Json));
        }
        catch (JsonException)
        {
        }
    }

    public bool HasSession => Token is { Length: > 0 };

    public string? Token
    {
        get
        {
            LoadTokenOnce();
            return _token;
        }
    }

    private void LoadTokenOnce()
    {
        if (_tokenLoaded)
            return;
        _tokenLoaded = true;
        _token = File.Exists(_tokenPath) ? Unprotect(File.ReadAllBytes(_tokenPath)) : null;
    }

    public string? Username => ReadSession()?.Username;

    public string? UserId => ReadSession()?.UserId;

    public string? Avatar => ReadSession()?.Avatar;

    public void Save(string username, string userId, string? avatar, string token)
    {
        File.WriteAllBytes(_tokenPath, Protect(token));
        File.WriteAllText(_sessionPath, JsonSerializer.Serialize(
            new Session(username, userId, avatar), Json));
        InvalidateTokenCache();
    }

    public void Clear()
    {
        if (File.Exists(_tokenPath))
            File.Delete(_tokenPath);
        if (File.Exists(_sessionPath))
            File.Delete(_sessionPath);
        InvalidateTokenCache();
    }

    private void InvalidateTokenCache()
    {
        _token = null;
        _tokenLoaded = false;
    }

    private Session? ReadSession()
    {
        if (!File.Exists(_sessionPath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Session>(File.ReadAllText(_sessionPath), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static byte[] Protect(string value) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);

    private static string Unprotect(byte[] value) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(value, null, DataProtectionScope.CurrentUser));
}
