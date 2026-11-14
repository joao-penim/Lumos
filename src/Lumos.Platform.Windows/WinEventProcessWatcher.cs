using System.Runtime.InteropServices;
using Lumos.Core;
using Microsoft.Win32.SafeHandles;
using static Lumos.Platform.Windows.Interop.Kernel32;
using static Lumos.Platform.Windows.Interop.User32;

namespace Lumos.Platform.Windows;

/// <summary>
/// Event-driven detection of watched apps, with no polling and no admin rights.
/// <para>Start: a WinEvent hook for top-level window creation (games create their window before their swapchain).
/// Exit: a thread-pool wait on the process handle (one shared wait thread, not one per process).</para>
/// </summary>
/// <remarks><see cref="Start"/> must be called on a thread that pumps messages: out-of-context WinEvents are delivered there.</remarks>
public sealed unsafe class WinEventProcessWatcher : IDisposable
{
    private static readonly TimeSpan FallbackExitPoll = TimeSpan.FromSeconds(2);
    private const long RecentWindowMs = 2000;

    private static WinEventProcessWatcher? _instance;

    private readonly HdrSessionManager _sessions;
    private readonly Lock _gate = new();
    private readonly Dictionary<uint, long> _recentPids = [];      // pid -> tick of last check, damps window-creation bursts
    private readonly Dictionary<uint, IDisposable> _tracked = [];  // pid -> exit wait
    private nint _hook;

    public WinEventProcessWatcher(HdrSessionManager sessions) => _sessions = sessions;

    public void Start()
    {
        if (_instance is not null) throw new InvalidOperationException("Only one watcher can be active.");
        _instance = this;
        _hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE, 0, &OnWinEvent, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_hook == 0) Log.Write("SetWinEventHook failed; only launches through Lumos will be detected.");
        Rescan();
    }

    /// <summary>Full snapshot of running processes. Called once at start and after settings change, never on a timer.</summary>
    public void Rescan()
    {
        lock (_gate) _recentPids.Clear();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == -1) return;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            for (var ok = Process32First(snap, ref entry); ok; ok = Process32Next(snap, ref entry))
            {
                var name = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(entry.szExeFile);
                if (_sessions.IsWatchedFileName(name)) Inspect(entry.th32ProcessID);
            }
        }
        finally { CloseHandle(snap); }
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Hot path: reject everything except new top-level windows as cheaply as possible.
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF || hwnd == 0) return;
        var self = _instance;
        if (self is null || GetAncestor(hwnd, GA_PARENT) != GetDesktopWindow()) return;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return;
        try { self.OnTopLevelWindow(pid); }
        catch (Exception e) { Log.Write($"Watcher error: {e.Message}"); }
    }

    private void OnTopLevelWindow(uint pid)
    {
        var now = Environment.TickCount64;
        lock (_gate)
        {
            if (_tracked.ContainsKey(pid)) return;
            if (_recentPids.TryGetValue(pid, out var last) && now - last < RecentWindowMs) return;
            if (_recentPids.Count > 512) _recentPids.Clear();
            _recentPids[pid] = now;
        }
        Inspect(pid);
    }

    private void Inspect(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, pid);
        var canWait = handle != 0;
        if (!canWait) handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid); // e.g. anti-cheat denies SYNCHRONIZE
        if (handle == 0) return;

        var keep = false;
        try
        {
            var path = GetProcessPath(handle);
            if (path is null || !_sessions.IsWatchedFileName(Path.GetFileName(path.AsSpan()))) return;

            lock (_gate)
            {
                if (_tracked.ContainsKey(pid)) return;
                if (!_sessions.TryStartProcessSession((int)pid, path)) return;
                _tracked[pid] = canWait ? new ExitWait(this, pid, handle) : new ExitPoll(this, pid, handle);
                keep = true;
            }
        }
        finally
        {
            if (!keep) CloseHandle(handle);
        }
    }

    private void OnExited(uint pid)
    {
        lock (_gate)
        {
            if (!_tracked.Remove(pid, out var waiter)) return;
            waiter.Dispose();
            _recentPids.Remove(pid);
        }
        _sessions.OnProcessExited((int)pid);
    }

    public void Dispose()
    {
        if (_hook != 0) UnhookWinEvent(_hook);
        _hook = 0;
        if (_instance == this) _instance = null;
        lock (_gate)
        {
            foreach (var w in _tracked.Values) w.Dispose();
            _tracked.Clear();
        }
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(nint handle) => SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
    }

    /// <summary>Signalled by the kernel when the process ends; costs nothing while it runs.</summary>
    private sealed class ExitWait : IDisposable
    {
        private readonly ProcessWaitHandle _handle;
        private readonly RegisteredWaitHandle _registration;

        public ExitWait(WinEventProcessWatcher owner, uint pid, nint process)
        {
            _handle = new ProcessWaitHandle(process);
            _registration = ThreadPool.RegisterWaitForSingleObject(_handle,
                static (state, _) => { var (o, p) = ((WinEventProcessWatcher, uint))state!; o.OnExited(p); },
                (owner, pid), Timeout.Infinite, executeOnlyOnce: true);
        }

        public void Dispose()
        {
            _registration.Unregister(null);
            _handle.Dispose();
        }
    }

    /// <summary>Fallback when the handle can't be waited on: checks this one process every 2 s, only while it runs.</summary>
    private sealed class ExitPoll : IDisposable
    {
        private readonly nint _process;
        private readonly Timer _timer;
        private int _disposed;

        public ExitPoll(WinEventProcessWatcher owner, uint pid, nint process)
        {
            _process = process;
            _timer = new Timer(_ =>
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                if (!GetExitCodeProcess(_process, out var code) || code != STILL_ACTIVE) owner.OnExited(pid);
            }, null, FallbackExitPoll, FallbackExitPoll);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _timer.Dispose();
            CloseHandle(_process);
        }
    }
}
