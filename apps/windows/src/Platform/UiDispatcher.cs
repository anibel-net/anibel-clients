using Microsoft.UI.Dispatching;

namespace Anibel.App.Services;

/// <summary>
/// Marshals actions onto the UI <see cref="DispatcherQueue"/>. With no queue
/// (tests, background threads) actions run inline on the calling thread.
/// </summary>
public sealed class UiDispatcher
{
    private readonly DispatcherQueue? _ui;

    /// <summary>Inline dispatcher: no UI queue, actions run on the calling thread.</summary>
    public UiDispatcher()
    {
    }

    public UiDispatcher(DispatcherQueue? ui) => _ui = ui;

    /// <summary>Dispatcher bound to the current thread's queue, if any.</summary>
    public static UiDispatcher ForCurrentThread() => new(TryUiQueue());

    private static DispatcherQueue? TryUiQueue()
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Post(Action action) => _ = PostAsync(action);

    public Task PostAsync(Action action)
    {
        if (_ui is { HasThreadAccess: false } ui)
        {
            var tcs = new TaskCompletionSource();
            if (!ui.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
            {
                action();
                return Task.CompletedTask;
            }
            return tcs.Task;
        }
        action();
        return Task.CompletedTask;
    }
}
