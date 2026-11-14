using System.Collections.Frozen;

namespace Lumos.Core;

/// <summary>
/// Decides when HDR goes on and off. Driven purely by events (process started/exited, launch requested);
/// the only timer is a one-shot grace delay after the last watched app on a display exits.
/// </summary>
/// <remarks>
/// Rules:
/// <list type="bullet">
/// <item>HDR is turned on for a display when the first watched app targeting it starts, unless it is already on.</item>
/// <item>Only displays Lumos turned on ("owned") are ever turned back off, after the last app exits plus the grace delay.</item>
/// <item>If the user switched HDR off by hand meanwhile, ownership is simply dropped.</item>
/// <item>Ownership is persisted so a crashed/killed agent can restore SDR on its next start.</item>
/// </list>
/// Thread-safe: callbacks may arrive from the message thread, the thread pool, or the IPC server.
/// </remarks>
public sealed class HdrSessionManager : IDisposable
{
    private static readonly TimeSpan PendingLaunchTimeout = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private readonly IHdrController _hdr;
    private readonly IStateStore _state;
    private readonly ITimerFactory _timers;

    private LumosSettings _settings = new();
    private volatile FrozenDictionary<string, WatchedApp[]> _byExeName = FrozenDictionary<string, WatchedApp[]>.Empty;

    private readonly Dictionary<int, Session> _processSessions = [];
    private readonly Dictionary<string, Session> _pendingLaunches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _refCount = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _owned = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IDisposable> _graceTimers = new(StringComparer.OrdinalIgnoreCase);
    private bool _paused;
    private bool _disposed;

    private sealed class Session(WatchedApp app, int pid, string[] displays)
    {
        public WatchedApp App { get; } = app;
        public int Pid { get; } = pid;
        public string[] Displays { get; } = displays;
        public IDisposable? Timeout { get; set; }
    }

    /// <summary>Raised (outside the lock) after HDR state, ownership or sessions change.</summary>
    public event Action? StateChanged;

    public HdrSessionManager(IHdrController hdr, IStateStore state, ITimerFactory timers)
    {
        _hdr = hdr;
        _state = state;
        _timers = timers;
        foreach (var id in state.LoadOwnedDisplays()) _owned.Add(id);
    }

    public bool IsPaused { get { lock (_gate) return _paused; } }

    public bool HasActiveHdr { get { lock (_gate) return _owned.Count > 0; } }

    public void ApplySettings(LumosSettings settings)
    {
        var map = settings.Apps
            .Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.ExePath))
            .GroupBy(a => a.ExeFileName, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        bool changed;
        lock (_gate)
        {
            _settings = settings;
            _byExeName = map;
            changed = SetPausedLocked(settings.Paused);
        }
        if (changed) RaiseChanged();
    }

    public WatchedApp? FindApp(string appId)
    {
        lock (_gate) return _settings.Apps.FirstOrDefault(a => a.Id == appId);
    }

    /// <summary>Hot path, lock-free and allocation-free: is any watched app named like this?</summary>
    public bool IsWatchedFileName(ReadOnlySpan<char> fileName) =>
        _byExeName.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(fileName);

    /// <summary>Starts a session if <paramref name="exePath"/> belongs to a watched app. Returns true if the caller should report the process's exit.</summary>
    public bool TryStartProcessSession(int pid, string exePath)
    {
        var fileName = Path.GetFileName(exePath);
        if (!_byExeName.TryGetValue(fileName, out var candidates)) return false;
        var app = candidates.FirstOrDefault(a =>
            a.MatchByNameOnly || string.Equals(a.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        if (app is null) return false;

        lock (_gate)
        {
            if (_disposed || _processSessions.ContainsKey(pid)) return false;
            var displays = DisplayTargetResolver.Resolve(app, _settings, _hdr.GetDisplays());
            _processSessions[pid] = new Session(app, pid, displays);
            Log.Write($"Started: {app.DisplayName} (pid {pid}) -> [{string.Join(", ", displays)}]");
            AcquireLocked(displays);

            // A "Launch with Lumos" hold for this app is now covered by the real process.
            if (_pendingLaunches.Remove(app.Id, out var pending))
            {
                pending.Timeout?.Dispose();
                ReleaseLocked(pending.Displays);
            }
        }
        RaiseChanged();
        return true;
    }

    public void OnProcessExited(int pid)
    {
        lock (_gate)
        {
            if (!_processSessions.Remove(pid, out var session)) return;
            Log.Write($"Exited: {session.App.DisplayName} (pid {pid})");
            ReleaseLocked(session.Displays);
        }
        RaiseChanged();
    }

    /// <summary>
    /// Turns HDR on ahead of launching <paramref name="app"/> and holds it until the app's process appears
    /// (or 60 s pass). Returns the displays switched.
    /// </summary>
    public string[] BeginLaunch(WatchedApp app)
    {
        string[] displays;
        lock (_gate)
        {
            if (_disposed) return [];
            displays = DisplayTargetResolver.Resolve(app, _settings, _hdr.GetDisplays());
            if (_pendingLaunches.Remove(app.Id, out var previous))
            {
                previous.Timeout?.Dispose();
                ReleaseLocked(previous.Displays);
            }
            var session = new Session(app, 0, displays);
            session.Timeout = _timers.Schedule(PendingLaunchTimeout, () => ExpirePendingLaunch(session));
            _pendingLaunches[app.Id] = session;
            AcquireLocked(displays);
        }
        RaiseChanged();
        return displays;
    }

    /// <summary>Drops a launch hold early, e.g. because starting the app failed.</summary>
    public void CancelLaunch(string appId)
    {
        lock (_gate)
        {
            if (!_pendingLaunches.Remove(appId, out var pending)) return;
            pending.Timeout?.Dispose();
            ReleaseLocked(pending.Displays);
        }
        RaiseChanged();
    }

    private void ExpirePendingLaunch(Session session)
    {
        lock (_gate)
        {
            if (!_pendingLaunches.TryGetValue(session.App.Id, out var current) || current != session) return;
            _pendingLaunches.Remove(session.App.Id);
            Log.Write($"Launch hold expired: {session.App.DisplayName}");
            ReleaseLocked(session.Displays);
        }
        RaiseChanged();
    }

    /// <summary>After the initial scan of running processes: turn off displays left owned by a previous (crashed) agent.</summary>
    public void RecoverStaleOwnership()
    {
        lock (_gate)
        {
            foreach (var id in _owned.ToArray())
            {
                if (_refCount.GetValueOrDefault(id) == 0)
                {
                    Log.Write($"Recovering stale HDR ownership of {id}");
                    TurnOffIfOwnedLocked(id);
                }
            }
        }
        RaiseChanged();
    }

    /// <summary>A manual toggle from the UI. Turning an owned display off drops ownership; turning one on never takes it.</summary>
    public bool SetHdrManually(string displayId, bool enabled)
    {
        bool ok;
        lock (_gate)
        {
            ok = _hdr.SetHdr(displayId, enabled);
            if (ok && !enabled && _owned.Remove(displayId))
            {
                CancelGraceLocked(displayId);
                PersistLocked();
            }
        }
        RaiseChanged();
        return ok;
    }

    public void SetPaused(bool paused)
    {
        bool changed;
        lock (_gate) changed = SetPausedLocked(paused);
        if (changed) RaiseChanged();
    }

    public AgentStatus GetStatus()
    {
        var displays = _hdr.GetDisplays();
        lock (_gate)
        {
            return new AgentStatus
            {
                Paused = _paused,
                Displays = [.. displays.Select(d => new DisplayStatus
                {
                    Id = d.Id,
                    FriendlyName = d.FriendlyName,
                    IsPrimary = d.IsPrimary,
                    HdrSupported = d.HdrSupported,
                    HdrEnabled = d.HdrEnabled,
                    OwnedByLumos = _owned.Contains(d.Id),
                })],
                Sessions = [.. _processSessions.Values.Concat(_pendingLaunches.Values).Select(s => new SessionStatus
                {
                    AppId = s.App.Id,
                    AppName = s.App.DisplayName,
                    ProcessId = s.Pid,
                    PendingLaunch = s.Pid == 0,
                    DisplayIds = [.. s.Displays],
                })],
            };
        }
    }

    /// <summary>Restores SDR on every display Lumos owns. Called when the agent exits.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var p in _pendingLaunches.Values) p.Timeout?.Dispose();
            _pendingLaunches.Clear();
            foreach (var id in _owned.ToArray()) TurnOffIfOwnedLocked(id);
        }
    }

    // ---- internals (call with _gate held) ----

    private bool SetPausedLocked(bool paused)
    {
        if (_paused == paused) return false;
        _paused = paused;
        Log.Write(paused ? "Paused" : "Resumed");
        if (paused)
        {
            foreach (var id in _owned.ToArray()) TurnOffIfOwnedLocked(id);
        }
        else
        {
            IReadOnlyList<DisplayInfo>? displays = null;
            foreach (var (id, count) in _refCount)
                if (count > 0) EnsureOnLocked(id, displays ??= _hdr.GetDisplays());
        }
        return true;
    }

    private void AcquireLocked(string[] displayIds)
    {
        IReadOnlyList<DisplayInfo>? displays = null;
        foreach (var id in displayIds)
        {
            var count = _refCount.GetValueOrDefault(id);
            _refCount[id] = count + 1;
            if (count > 0) continue;
            if (CancelGraceLocked(id)) continue; // still owned and still on: app restarted within the grace period
            if (!_paused) EnsureOnLocked(id, displays ??= _hdr.GetDisplays());
        }
    }

    private void ReleaseLocked(string[] displayIds)
    {
        foreach (var id in displayIds)
        {
            var count = _refCount.GetValueOrDefault(id) - 1;
            if (count > 0) { _refCount[id] = count; continue; }
            _refCount.Remove(id);
            if (_owned.Contains(id)) ScheduleOffLocked(id);
        }
    }

    private void EnsureOnLocked(string id, IReadOnlyList<DisplayInfo> displays)
    {
        var info = displays.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (info is null || info.HdrEnabled) return; // already on (by the user, or still ours after a restart)
        if (_hdr.SetHdr(id, true))
        {
            Log.Write($"HDR on: {info.FriendlyName}");
            if (_owned.Add(id)) PersistLocked();
        }
        else
        {
            Log.Write($"Failed to turn HDR on: {info.FriendlyName}");
        }
    }

    private void ScheduleOffLocked(string id)
    {
        CancelGraceLocked(id);
        var delay = TimeSpan.FromSeconds(Math.Max(0, _settings.GraceDelaySeconds));
        if (delay == TimeSpan.Zero)
        {
            TurnOffIfOwnedLocked(id);
            return;
        }

        IDisposable? timer = null;
        timer = _timers.Schedule(delay, () =>
        {
            lock (_gate)
            {
                if (!_graceTimers.TryGetValue(id, out var current) || current != timer) return;
                _graceTimers.Remove(id);
                timer.Dispose();
                if (_refCount.GetValueOrDefault(id) == 0) TurnOffIfOwnedLocked(id);
            }
            RaiseChanged();
        });
        _graceTimers[id] = timer;
    }

    private bool CancelGraceLocked(string id)
    {
        if (!_graceTimers.Remove(id, out var timer)) return false;
        timer.Dispose();
        return true;
    }

    private void TurnOffIfOwnedLocked(string id)
    {
        CancelGraceLocked(id);
        if (!_owned.Remove(id)) return;
        PersistLocked();
        var info = _hdr.GetDisplays().FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (info is { HdrEnabled: true })
        {
            var ok = _hdr.SetHdr(id, false);
            Log.Write(ok ? $"HDR off: {info.FriendlyName}" : $"Failed to turn HDR off: {info.FriendlyName}");
        }
    }

    private void PersistLocked() => _state.SaveOwnedDisplays([.. _owned]);

    private void RaiseChanged()
    {
        try { StateChanged?.Invoke(); }
        catch (Exception e) { Log.Write($"StateChanged handler failed: {e.Message}"); }
    }
}
