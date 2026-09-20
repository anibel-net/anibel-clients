using CommunityToolkit.Mvvm.ComponentModel;

namespace Anibel.App.Services;

public enum AppUpdateState { Unavailable, Idle, UpToDate, Checking, Downloading, Ready, Failed }

/// <summary>One update operation at a time; only an explicit restart applies it.</summary>
public sealed class AppUpdateService : ObservableObject, IDisposable
{
    private readonly IReleaseUpdateSource _source;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _loop = Task.CompletedTask;
    private Task _operation = Task.CompletedTask;
    private bool _started;
    private AppUpdateState _state;
    private int _progress;
    private string _version = "";
    public AppUpdateService(IReleaseUpdateSource source)
    {
        _source = source;
        _version = source.PendingVersion ?? "";
        _state = !source.IsInstalled ? AppUpdateState.Unavailable : _version.Length > 0 ? AppUpdateState.Ready : AppUpdateState.Idle;
    }
    public AppUpdateState State => _state;
    public bool IsReady => State == AppUpdateState.Ready;
    public bool IsBusy => State is AppUpdateState.Checking or AppUpdateState.Downloading;
    public bool CanCheck => State is AppUpdateState.Idle or AppUpdateState.UpToDate or AppUpdateState.Failed;
    public int Progress => _progress;
    public bool IsDownloading => State == AppUpdateState.Downloading;
    public string VersionLabel => $"Anibel.Net {ReleaseUpdateSource.CurrentVersion}";
    public string ChannelLabel => ReleaseUpdateSource.IsPreview ? "Папярэднія версіі" : "Стабільныя версіі";
    public string StatusLabel => State switch
    {
        AppUpdateState.Unavailable => "Для аўтаматычных абнаўленняў усталюйце праграму праз Setup.exe.",
        AppUpdateState.UpToDate => "Усталявана апошняя версія.",
        AppUpdateState.Checking => "Праверка абнаўленняў…",
        AppUpdateState.Downloading => $"Спампоўваецца версія {_version}: {Progress}%",
        AppUpdateState.Ready => $"Версія {_version} гатовая. Перазапусціце праграму для абнаўлення.",
        AppUpdateState.Failed => "Не ўдалося праверыць або спампаваць абнаўленне. Паспрабуйце пазней.",
        _ => "Абнаўленні правяраюцца аўтаматычна.",
    };
    private void SetState(AppUpdateState state) { _state = state; OnPropertyChanged(string.Empty); }
    public void Start()
    {
        if (_started || State == AppUpdateState.Unavailable) return;
        _started = true;
        _loop = RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _lifetime.Token);
            while (!_lifetime.IsCancellationRequested)
            {
                await CheckAsync();
                await Task.Delay(TimeSpan.FromHours(6), _lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    public Task CheckAsync()
    {
        if (!CanCheck || _lifetime.IsCancellationRequested) return _operation;
        return _operation = CheckCoreAsync();
    }
    private async Task CheckCoreAsync()
    {
        SetState(AppUpdateState.Checking);
        try
        {
            _version = await _source.CheckAsync().WaitAsync(_lifetime.Token) ?? "";
            _lifetime.Token.ThrowIfCancellationRequested();
            if (_version.Length == 0) { SetState(AppUpdateState.UpToDate); return; }
            _progress = 0;
            SetState(AppUpdateState.Downloading);
            var progress = new Progress<int>(value =>
            {
                if (State != AppUpdateState.Downloading || _lifetime.IsCancellationRequested) return;
                _progress = Math.Clamp(value, 0, 100);
                OnPropertyChanged(string.Empty);
            });
            await _source.DownloadAsync(value => ((IProgress<int>)progress).Report(value), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            SetState(AppUpdateState.Ready);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Diag.Log($"Update failed: {ex}"); SetState(AppUpdateState.Failed); }
    }
    public void PrepareRestart()
    {
        if (!IsReady) throw new InvalidOperationException("No update is ready.");
        _source.PrepareRestart();
    }
    public async Task StopAsync()
    {
        _lifetime.Cancel();
        await Task.WhenAll(_loop, _operation);
    }
    public void Dispose() { _lifetime.Cancel(); }
}
