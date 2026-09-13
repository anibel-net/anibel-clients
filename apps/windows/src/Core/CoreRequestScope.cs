namespace Anibel.App.Core;

/// <summary>Passes explicit refresh intent to Rust. Contains no cache policy or data.</summary>
public static class CoreRequestScope
{
    private static readonly AsyncLocal<bool> ReloadRequested = new();

    public static bool IsReload => ReloadRequested.Value;

    public static IDisposable Reload()
    {
        var previous = ReloadRequested.Value;
        ReloadRequested.Value = true;
        return new Scope(previous);
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReloadRequested.Value = previous;
        }
    }
}
