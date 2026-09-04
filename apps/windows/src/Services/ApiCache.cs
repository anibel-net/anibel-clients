using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Anibel.App.Services;

/// <summary>
/// Disk+memory cache for GraphQL/FFI reads. Local-first: a fresh hit never
/// touches the network; a stale hit is served immediately while a background
/// fetch rewrites the entry. Mutations call <see cref="Bypass"/> / Invalidate.
/// </summary>
public sealed class ApiCache
{
    private static readonly AsyncLocal<int> BypassDepth = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _root;
    private readonly ConcurrentDictionary<string, Entry> _memory = new();

    public ApiCache(string? rootDirectory = null)
    {
        _root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Anibel", "cache");
        Directory.CreateDirectory(_root);
    }

    public static bool IsBypassed => BypassDepth.Value > 0;

    public static IDisposable Bypass()
    {
        BypassDepth.Value++;
        return new BypassScope();
    }

    public bool TryGet(string op, string argsJson, out string valueJson, out bool stale)
    {
        valueJson = "";
        stale = false;
        var key = Key(op, argsJson);
        if (!_memory.TryGetValue(key, out var entry))
        {
            entry = ReadDisk(key);
            if (entry is null)
            {
                return false;
            }
            _memory[key] = entry;
        }

        var age = DateTimeOffset.UtcNow - entry.StoredAt;
        if (age > Ttl(op).Keep)
        {
            return false;
        }
        valueJson = entry.Json;
        stale = age > Ttl(op).Fresh;
        return true;
    }

    public event Action<string>? Refreshed;

    public void Set(string op, string argsJson, string valueJson)
    {
        var key = Key(op, argsJson);
        var entry = new Entry(valueJson, DateTimeOffset.UtcNow);
        _memory[key] = entry;
        try
        {
            File.WriteAllText(Path.Combine(_root, key + ".json"), JsonSerializer.Serialize(entry, Json));
        }
        catch (Exception ex)
        {
            Diag.Log($"api cache write: {ex.Message}");
        }
        Refreshed?.Invoke(op);
    }

    public void Invalidate(params string[] ops)
    {
        foreach (var op in ops)
        {
            foreach (var key in _memory.Keys.Where(k => k.StartsWith(op + "_", StringComparison.Ordinal)).ToList())
            {
                _memory.TryRemove(key, out _);
            }
            try
            {
                foreach (var file in Directory.EnumerateFiles(_root, op + "_*.json"))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    public void Clear()
    {
        _memory.Clear();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
            Directory.CreateDirectory(_root);
        }
        catch (Exception ex)
        {
            Diag.Log($"api cache clear: {ex.Message}");
        }
    }

    public static bool IsCacheable(string op) => op switch
    {
        "login" or "logout" or "setToken" or "version" or "health"
            or "markAs" or "removeMark" or "addFavorite" or "removeFavorite"
            or "addHistoryRecord" or "removeHistoryRecord" or "addComment"
            or "resolveEpisode" => false,
        _ => true,
    };

    public static string[] InvalidatedBy(string op) => op switch
    {
        "addComment" => ["comments"],
        "markAs" or "removeMark" => ["marks", "status", "media"],
        "addFavorite" or "removeFavorite" => ["favorites", "media"],
        "addHistoryRecord" or "removeHistoryRecord" => ["media", "episodes", "episodesMatrix"],
        "login" or "logout" or "setToken" => ["media", "favorites", "marks", "status", "comments"],
        _ => [],
    };

    private Entry? ReadDisk(string key)
    {
        var path = Path.Combine(_root, key + ".json");
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<Entry>(File.ReadAllText(path), Json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Key(string op, string argsJson)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(op + "\n" + argsJson)));
        return op + "_" + hash[..16];
    }

    private static (TimeSpan Fresh, TimeSpan Keep) Ttl(string op) => op switch
    {
        "comments" => (TimeSpan.FromMinutes(15), TimeSpan.FromDays(2)),
        "user" or "favorites" or "marks" or "status" => (TimeSpan.FromMinutes(10), TimeSpan.FromDays(2)),
        "updates" or "slider" or "trends" => (TimeSpan.FromHours(6), TimeSpan.FromDays(7)),
        _ => (TimeSpan.FromHours(12), TimeSpan.FromDays(14)),
    };

    private sealed record Entry(string Json, DateTimeOffset StoredAt);

    private sealed class BypassScope : IDisposable
    {
        public void Dispose()
        {
            var n = BypassDepth.Value;
            BypassDepth.Value = n > 0 ? n - 1 : 0;
        }
    }
}
