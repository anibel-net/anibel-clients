using Anibel.App.Core;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace Anibel.App.Services;

/// <summary>Owns polling and waits for the last update before core shutdown.</summary>
internal sealed class CoreStatePump
{
    private readonly CoreClient _core;
    private readonly SessionService _session;
    private readonly DownloadService _downloads;
    private readonly DispatcherQueueTimer _timer;
    private Task _update = Task.CompletedTask;

    public CoreStatePump(IServiceProvider services)
    {
        _core = services.GetRequiredService<CoreClient>();
        _session = services.GetRequiredService<SessionService>();
        _downloads = services.GetRequiredService<DownloadService>();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();
    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_update.IsCompleted) _update = RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        try
        {
            _ = _core.DrainEvents();
            var revision = _session.Revision;
            await _session.RefreshAsync();
            if (revision != _session.Revision) WeakReferenceMessenger.Default.Send(new SessionChangedMessage());
            await _downloads.RefreshAsync();
        }
        catch (Exception ex) { Diag.Log($"core state update: {ex.Message}"); }
    }
    public async Task StopAsync()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        await _update;
    }
}
