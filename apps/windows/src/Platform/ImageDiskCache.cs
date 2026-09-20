using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Anibel.App.Services;

/// <summary>Immutable images keyed by their complete URL, including its query string.</summary>
internal sealed class ImageDiskCache(string directory, HttpClient client, long capacity = 512L * 1024 * 1024)
{
    internal static ImageDiskCache Shared { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Anibel", "cache", "images"),
        new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
    private const int MaxImageBytes = 16 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _downloads = new(4);
    private readonly object _files = new();
    private readonly CancellationTokenSource _lifetime = new();

    private string FileName(string url) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".image");

    public async Task<string> GetAsync(string url)
    {
        _lifetime.Token.ThrowIfCancellationRequested();
        var pending = _pending.GetOrAdd(url, key => new Lazy<Task<string>>(() => DownloadAsync(key)));
        try { return await pending.Value.ConfigureAwait(false); }
        finally { _pending.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(url, pending)); }
    }

    public async Task StopAsync()
    {
        await _lifetime.CancelAsync();
        try { await Task.WhenAll(_pending.Values.Where(p => p.IsValueCreated).Select(p => p.Value)); }
        catch (Exception) { /* Each image caller handles its failure. */ }
    }

    public void Invalidate(string url)
    {
        try { lock (_files) File.Delete(FileName(url)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<string> DownloadAsync(string url)
    {
        var path = FileName(url);
        if (File.Exists(path)) return path;
        await _downloads.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(directory);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _lifetime.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxImageBytes)
                throw new InvalidDataException("Image exceeds the cache entry limit.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            long length = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    length += read;
                    if (length > MaxImageBytes) throw new InvalidDataException("Image exceeds the cache entry limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                }
            }
            if (length == 0) throw new InvalidDataException("Empty image response.");
            lock (_files)
            {
                // No TTL or HTTP revalidation. Evict oldest downloads only when the size limit is reached.
                var files = new DirectoryInfo(directory).GetFiles("*.image").OrderBy(f => f.LastWriteTimeUtc).ToArray();
                var total = files.Sum(f => f.Length);
                foreach (var old in files)
                {
                    if (total <= Math.Max(0, capacity - length)) break;
                    try { File.Delete(old.FullName); total -= old.Length; } catch (IOException) { }
                }
                File.Move(temporary, path, overwrite: true);
            }
            return path;
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _downloads.Release();
        }
    }
}
