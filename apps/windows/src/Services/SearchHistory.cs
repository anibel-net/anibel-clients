using System.Text.Json;

namespace Anibel.App.Services;

public sealed class SearchHistory
{
    public const int MaxItems = 8;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly List<string> _items = [];
    private readonly object _gate = new();

    public SearchHistory(string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Anibel");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "search-history.json");
        }
        else
        {
            _path = path;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        Load();
    }

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public void Add(string query)
    {
        var q = query.Trim();
        if (q.Length < 2)
        {
            return;
        }
        lock (_gate)
        {
            _items.RemoveAll(s => string.Equals(s, q, StringComparison.OrdinalIgnoreCase));
            _items.Insert(0, q);
            while (_items.Count > MaxItems)
            {
                _items.RemoveAt(_items.Count - 1);
            }
            Save();
        }
    }

    public void Remove(string query)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(s => string.Equals(s, query.Trim(), StringComparison.OrdinalIgnoreCase)) > 0)
            {
                Save();
            }
        }
    }

    public IReadOnlyList<string> Matches(string query)
    {
        var q = query.Trim();
        lock (_gate)
        {
            if (q.Length == 0)
            {
                return [.. _items];
            }
            return _items
                .Where(s => s.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }
        try
        {
            var loaded = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path), Json);
            if (loaded is null)
            {
                return;
            }
            lock (_gate)
            {
                _items.Clear();
                foreach (var item in loaded)
                {
                    var q = item.Trim();
                    if (q.Length >= 2 && !_items.Contains(q, StringComparer.OrdinalIgnoreCase))
                    {
                        _items.Add(q);
                    }
                    if (_items.Count >= MaxItems)
                    {
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
    }

    private void Save()
    {
        try
        {
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_items, Json);
            }
            File.WriteAllText(_path, json);
        }
        catch (IOException)
        {
        }
    }
}
