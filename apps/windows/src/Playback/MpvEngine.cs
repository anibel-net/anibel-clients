using Anibel.App.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Anibel.App.Playback;

/// <summary>
/// libmpv engine for Anibel-hosted (native) playback.
///
/// Requires the patched mpv build (zhongfly/mpv-winbuild) with the d3d11
/// composition mode (see scripts/fetch-mpv.ps1):
///
///   gpu-api=d3d11, d3d11-output-mode=composition, d3d11-composition-size=WxH,
///   and the `display-swapchain` property (INT64 → IDXGISwapChain*).
///
/// Render flow: mpv creates a composition swapchain → bound to
/// SwapChainPanel via ISwapChainPanelNative. libass subtitles are rendered by
/// mpv into the same frames; fonts loaded from the engine's fonts directory
/// (config-files: placed into &lt;workDir&gt;/fonts/).
///
/// Threading: all events (Ready/PositionChanged/…) are marshaled onto the
/// UI thread's DispatcherQueue — the mpv event thread must never touch XAML.
/// </summary>
public sealed class MpvEngine : IPlayerEngine, IPlaybackControls
{
    private IntPtr _mpv;
    private Thread? _eventThread;
    private volatile bool _running;
    private SwapChainPanel? _panel;
    private DispatcherQueue? _dispatcher;
    private volatile bool _swapChainBound;
    private uint _width;
    private uint _height;
    private double _lastReportedPos = -1;
    // Cross-thread: _pendingSubs is assigned on the UI thread (Load) and read
    // on the mpv event thread (ApplyPendingSubs). volatile so the event thread
    // never observes a stale list reference; the list itself is never mutated
    // after publication.
    private volatile IReadOnlyList<string> _pendingSubs = [];
    private bool _subsApplied;
    // Cross-thread: _preferDubAudio is written on the UI thread
    // (PreferDubAudio/Initialize) and read on the mpv event thread
    // (ApplyPendingSubs/ApplySubtitlePreference/ApplyAudioPreference).
    private volatile bool _preferDubAudio;

    public event Action? Ready;
    public event Action? Ended;         // reached end of file (EOF reason)
    public event Action<string>? Error;
    public event Action<double, double>? PositionChanged; // seconds, duration
    public event Action<bool>? PauseChanged;

    public bool IsInitialized => _mpv != IntPtr.Zero;

    /// <summary>True when the last Load() carried external subtitle files.</summary>
    public bool HasSubs { get; private set; }

    /// <summary>
    /// configDir is the directory whose <c>fonts/</c> subdirectory mpv/libass
    /// loads custom fonts from (shared cache across episodes).
    /// </summary>
    public void Initialize(SwapChainPanel panel, string configDir, uint width = 1920, uint height = 1080)
    {
        if (_mpv != IntPtr.Zero)
        {
            return;
        }

        _panel = panel;
        _dispatcher = panel.DispatcherQueue;
        _width = width;
        _height = height;
        _swapChainBound = false;
        _lastReportedPos = -1;
        durationCache = null;
        _pendingSubs = [];
        _subsApplied = false;
        HasSubs = false;

        _mpv = MpvNative.mpv_create();
        if (_mpv == IntPtr.Zero)
        {
            throw new InvalidOperationException("mpv_create failed (libmpv-2.dll present?)");
        }

        Option("config", "no");
        Option("osc", "no");
        Option("idle", "yes");
        Option("force-window", "yes");          // vo needs a window context in composition mode
        Option("auto-window-resize", "no");
        Option("gpu-api", "d3d11");
        Option("d3d11-output-mode", "composition");
        Option("d3d11-composition-size", $"{width}x{height}");
        Option("config-dir", configDir);         // fonts/ resolved against this dir
        Option("screenshot-dir", configDir);
        Option("log-file", Path.Combine(configDir, "mpv.log")); // playback diagnostics
        Option("hwdec", "d3d11va");
        Option("sub-auto", "no");                // explicit sub-add after file loads
        Option("sub-visibility", "yes");
        Option("sid", "no");                     // pick after load — dub must not auto-select dialogue
        Option("aid", "auto");
        Option("alang", _preferDubAudio ? "be,bel,be-BY" : "jpn,ja,jp,und");
        Option("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        Option("volume", "100");

        if (MpvNative.mpv_initialize(_mpv) != 0)
        {
            var err = new InvalidOperationException("mpv_initialize failed");
            DestroyMpv();
            throw err;
        }

        MpvNative.mpv_observe_property(_mpv, 1, "pause", MpvNative.MpvFormatFlag);
        MpvNative.mpv_observe_property(_mpv, 2, "time-pos", MpvNative.MpvFormatDouble);
        MpvNative.mpv_observe_property(_mpv, 3, "duration", MpvNative.MpvFormatDouble);

        _running = true;
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _eventThread.Start();
    }

    public void Resize(uint width, uint height)
    {
        _width = width;
        _height = height;
        if (_mpv != IntPtr.Zero)
        {
            Option("d3d11-composition-size", $"{width}x{height}");
        }
    }

    public void InvalidateSurface()
    {
        _swapChainBound = false;
        if (_panel is not null)
        {
            try
            {
                SwapChainBinder.Detach(_panel);
            }
            catch
            {
            }
        }
        if (_mpv != IntPtr.Zero)
        {
            Option("d3d11-composition-size", $"{_width}x{_height}");
            TryBindSwapChain();
        }
    }

    /// <summary>Call before Initialize/Load. Dub muxes original + Belarusian.</summary>
    public void PreferDubAudio(bool dub)
    {
        _preferDubAudio = dub;
        if (_mpv != IntPtr.Zero)
        {
            CommandString(dub ? "set alang be,bel,be-BY" : "set alang jpn,ja,jp,und");
        }
    }

    /// <summary>
    /// Starts playback. External subtitles are NOT added here — mpv rejects
    /// sub-add while the file is still loading; they are applied on the
    /// file-loaded event (ApplyPendingSubs).
    /// </summary>
    public void Load(PlaybackIntentDto intent, IReadOnlyList<string> subPaths)
    {
        if (_mpv == IntPtr.Zero)
        {
            throw new InvalidOperationException("engine not initialized");
        }
        // a new file may carry a fresh swapchain — allow rebind
        _swapChainBound = false;
        _pendingSubs = SubtitlePicker.Filter(subPaths, _preferDubAudio);
        _subsApplied = false;
        durationCache = null;
        HasSubs = _pendingSubs.Count > 0;

        if (intent.VideoSrc is { } src)
        {
            MpvNative.Command(_mpv, "loadfile", src);
        }
        else
        {
            HasSubs = false;
        }
        MpvNative.CommandString(_mpv, "set pause no");
    }

    public Task LoadAsync(PlaybackIntentDto intent, IReadOnlyList<string> subPaths, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Load(intent, subPaths);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // transport / audio / subs — every accessor must tolerate a torn-down
    // engine (_mpv == IntPtr.Zero): touching mpv with a null handle is a
    // native access violation, not a managed exception
    // ------------------------------------------------------------------

    public void TogglePause() => CommandString("cycle pause");
    public void SetPause(bool paused) => CommandString($"set pause {(paused ? "yes" : "no")}");

    public void Seek(double seconds)
    {
        if (_mpv != IntPtr.Zero)
        {
            MpvNative.SetDouble(_mpv, "time-pos", seconds);
        }
    }

    public void SetRate(double rate)
    {
        if (_mpv != IntPtr.Zero)
        {
            MpvNative.SetDouble(_mpv, "speed", rate);
        }
    }

    public void SetVolume(double volume)
    {
        if (_mpv != IntPtr.Zero)
        {
            MpvNative.SetDouble(_mpv, "volume", Math.Clamp(volume, 0, 100));
        }
    }

    public double Volume => _mpv != IntPtr.Zero ? MpvNative.GetDouble(_mpv, "volume") ?? 100 : 100;

    public void SetMute(bool mute)
    {
        if (_mpv != IntPtr.Zero)
        {
            MpvNative.SetFlag(_mpv, "mute", mute);
        }
    }

    public bool IsMuted => _mpv != IntPtr.Zero && (MpvNative.GetFlag(_mpv, "mute") ?? false);

    /// <summary>Current subtitle track id (0 = off, -1 = engine down).</summary>
    public long SubTrack => _mpv != IntPtr.Zero ? MpvNative.GetInt64(_mpv, "sid") ?? 0 : -1;

    public void SetSubTrack(long id)
    {
        if (_mpv != IntPtr.Zero)
        {
            MpvNative.SetInt64(_mpv, "sid", id);
        }
    }

    public double Position => _mpv != IntPtr.Zero ? MpvNative.GetDouble(_mpv, "time-pos") ?? -1 : -1;
    public double Duration => _mpv != IntPtr.Zero ? MpvNative.GetDouble(_mpv, "duration") ?? -1 : -1;

    private void Option(string name, string value)
    {
        if (MpvNative.mpv_set_option_string(_mpv, name, value) != 0)
        {
            Debug.WriteLine($"[mpv] option ignored: {name}={value}");
        }
    }

    private void CommandString(string cmd)
    {
        if (_mpv != IntPtr.Zero)
        {
            MpvNative.CommandString(_mpv, cmd);
        }
    }

    /// <summary>Marshals a callback onto the UI thread (no-op if already gone).</summary>
    private void OnUi(Action action)
    {
        _dispatcher?.TryEnqueue(() =>
        {
            if (_running)
            {
                action();
            }
        });
    }

    private void EventLoop(object? _)
    {
        while (_running)
        {
            var evtPtr = MpvNative.mpv_wait_event(_mpv, 1.0);
            if (evtPtr == IntPtr.Zero)
            {
                continue;
            }
            var evt = Marshal.PtrToStructure<MpvNative.MpvEvent>(evtPtr);
            switch (evt.event_id)
            {
                case MpvNative.EventFileLoaded:
                    ApplyPendingSubs();
                    ApplyAudioPreference();
                    OnUi(() => Ready?.Invoke());
                    break;

                case MpvNative.EventEndFile:
                    var endFile = evt.data != IntPtr.Zero
                        ? Marshal.PtrToStructure<MpvNative.MpvEventEndFile>(evt.data)
                        : default;
                    // reason 0 = MPV_END_FILE_REASON_EOF
                    if (endFile.reason == 0)
                    {
                        OnUi(() => Ended?.Invoke());
                    }
                    break;

                case MpvNative.EventVideoReconfig:
                    TryBindSwapChain();
                    break;

                case MpvNative.EventPlaybackRestart:
                    OnUi(() => Ready?.Invoke());
                    break;

                case MpvNative.EventPropertyChange:
                    var prop = Marshal.PtrToStructure<MpvNative.MpvEventProperty>(evt.data);
                    var name = Marshal.PtrToStringUTF8(prop.name) ?? "";
                    if (name == "time-pos" && prop.format == MpvNative.MpvFormatDouble)
                    {
                        var pos = Marshal.PtrToStructure<double>(prop.data);
                        // throttle: ~4 updates/s is plenty for the time label
                        if (Math.Abs(pos - _lastReportedPos) < 0.25)
                        {
                            break;
                        }
                        _lastReportedPos = pos;
                        var dur = durationCache ?? MpvNative.GetDouble(_mpv, "duration") ?? -1;
                        OnUi(() => PositionChanged?.Invoke(pos, dur));
                    }
                    else if (name == "duration" && prop.format == MpvNative.MpvFormatDouble)
                    {
                        durationCache = Marshal.PtrToStructure<double>(prop.data);
                    }
                    else if (name == "pause" && prop.format == MpvNative.MpvFormatFlag)
                    {
                        var paused = Marshal.PtrToStructure<int>(prop.data) != 0;
                        OnUi(() => PauseChanged?.Invoke(paused));
                    }
                    break;
            }
        }
    }

    // Cross-thread: durationCache is reset to null on the UI thread
    // (Initialize/Load) but written/read on the mpv event thread
    // (EventLoop). double? cannot be volatile; a torn reset is harmless —
    // worst case one extra mpv "duration" property read after a new load.
    private double? durationCache;

    /// <summary>
    /// sub-add only sticks once a file is actually loaded. Dub keeps signs
    /// only; dialogue files are never attached. Then sid is chosen from the
    /// realized track list (external + muxed).
    /// </summary>
    private void ApplyPendingSubs()
    {
        if (_subsApplied || _mpv == IntPtr.Zero)
        {
            return;
        }
        _subsApplied = true;
        for (var i = 0; i < _pendingSubs.Count; i++)
        {
            // select the first kept file immediately — track-list may not
            // include a just-added external yet, so we cannot rely on Pick.
            var flag = i == 0 ? "select" : "auto";
            MpvNative.Command(_mpv, "sub-add", _pendingSubs[i], flag);
        }
        if (_pendingSubs.Count > 0)
        {
            CommandString("set sub-visibility yes");
            return;
        }
        ApplySubtitlePreference();
    }

    private void ApplySubtitlePreference()
    {
        if (_mpv == IntPtr.Zero)
        {
            return;
        }
        var count = MpvNative.GetInt64(_mpv, "track-list/count") ?? 0;
        var tracks = new List<SubtitleTrackInfo>();
        for (var i = 0; i < count; i++)
        {
            var type = MpvNative.GetString(_mpv, $"track-list/{i}/type") ?? "";
            if (!string.Equals(type, "sub", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var id = MpvNative.GetInt64(_mpv, $"track-list/{i}/id") ?? 0;
            var title = MpvNative.GetString(_mpv, $"track-list/{i}/title") ?? "";
            var lang = MpvNative.GetString(_mpv, $"track-list/{i}/lang") ?? "";
            var file = MpvNative.GetString(_mpv, $"track-list/{i}/external-filename")
                ?? MpvNative.GetString(_mpv, $"track-list/{i}/filename")
                ?? "";
            tracks.Add(new SubtitleTrackInfo(id, title, lang, file));
        }
        var sid = SubtitlePicker.Pick(tracks, _preferDubAudio);
        if (sid is > 0)
        {
            MpvNative.SetInt64(_mpv, "sid", sid.Value);
            CommandString("set sub-visibility yes");
            Debug.WriteLine($"[mpv] sid={sid} dub={_preferDubAudio} subs={tracks.Count}");
        }
        else
        {
            CommandString("set sid no");
            CommandString("set sub-visibility no");
            Debug.WriteLine($"[mpv] sid=no dub={_preferDubAudio} subs={tracks.Count}");
        }
    }

    private void ApplyAudioPreference()
    {
        if (_mpv == IntPtr.Zero)
        {
            return;
        }
        var count = MpvNative.GetInt64(_mpv, "track-list/count") ?? 0;
        var tracks = new List<AudioTrackInfo>();
        for (var i = 0; i < count; i++)
        {
            var type = MpvNative.GetString(_mpv, $"track-list/{i}/type") ?? "";
            if (!string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var id = MpvNative.GetInt64(_mpv, $"track-list/{i}/id") ?? 0;
            var lang = MpvNative.GetString(_mpv, $"track-list/{i}/lang") ?? "";
            var title = MpvNative.GetString(_mpv, $"track-list/{i}/title") ?? "";
            tracks.Add(new AudioTrackInfo(id, lang, title));
        }
        var aid = AudioTrackPicker.Pick(tracks, _preferDubAudio);
        if (aid is > 0)
        {
            MpvNative.SetInt64(_mpv, "aid", aid.Value);
            Debug.WriteLine($"[mpv] aid={aid} dub={_preferDubAudio} tracks={tracks.Count}");
        }
    }

    private void TryBindSwapChain()
    {
        if (_swapChainBound || _panel is null || !_running)
        {
            return;
        }

        var swapChain = MpvNative.GetInt64(_mpv, "display-swapchain");
        if (swapChain is not > 0)
        {
            return; // not ready yet — re-tried on next VideoReconfig
        }

        _swapChainBound = true;
        _panel.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                SwapChainBinder.Attach(_panel!, new IntPtr(swapChain.Value));
            }
            catch (Exception ex)
            {
                _swapChainBound = false; // allow retry on the next reconfig
                Error?.Invoke($"swapchain: {ex.Message}");
            }
        });
    }

    private void DestroyMpv()
    {
        _running = false;
        if (_mpv != IntPtr.Zero)
        {
            // wakeup makes a pending mpv_wait_event return immediately, so a
            // plain Join cannot deadlock — and we must never terminate_destroy
            // while the event thread is still inside the handle.
            MpvNative.mpv_wakeup(_mpv);
            _eventThread?.Join();
            _eventThread = null;
            MpvNative.mpv_terminate_destroy(_mpv);
            _mpv = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        DestroyMpv();
        if (_panel is not null)
        {
            var panel = _panel;
            try
            {
                panel.DispatcherQueue.TryEnqueue(() => SwapChainBinder.Detach(panel));
            }
            catch
            {
                // best effort — panel may already be torn down
            }
        }
        _panel = null;
        _dispatcher = null;
    }
}
