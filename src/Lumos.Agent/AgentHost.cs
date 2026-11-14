using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumos.Core;
using Lumos.Platform.Windows;
using static Lumos.Platform.Windows.Interop.User32;

namespace Lumos.Agent;

/// <summary>
/// The always-on part of Lumos. One thread runs a blocking GetMessage loop that owns the hidden window,
/// the tray icon and the WinEvent hook; everything else is event callbacks.
/// </summary>
internal sealed class AgentHost : IDisposable
{
    private const string WindowClass = "Lumos.Agent.Window";
    private const uint WM_STATE_CHANGED = WM_APP + 2;
    private const uint CMD_OPEN = 1, CMD_PAUSE = 2, CMD_EXIT = 3;

    private static AgentHost? _current;

    private readonly WindowsHdrController _hdr = new();
    private readonly HdrSessionManager _sessions;
    private readonly WinEventProcessWatcher _watcher;
    private readonly IpcServer _ipc;
    private TrayIcon? _tray;
    private nint _hwnd;
    private uint _taskbarCreatedMsg;
    private int _statePosted;

    public AgentHost()
    {
        _sessions = new HdrSessionManager(_hdr, new JsonStateStore(), ThreadPoolTimerFactory.Instance);
        _watcher = new WinEventProcessWatcher(_sessions);
        _ipc = new IpcServer(HandleAsync);
    }

    public int Run(string? launchAppId)
    {
        _current = this;
        Log.Write($"Agent starting (pid {Environment.ProcessId})");
        _sessions.ApplySettings(SettingsStore.Load());

        CreateWindow();
        _tray = new TrayIcon(_hwnd);
        _tray.Show();

        _watcher.Start();                  // hook + initial scan of already-running apps
        _sessions.RecoverStaleOwnership(); // undo HDR left on by a crashed/killed agent
        _sessions.StateChanged += OnSessionsChanged;
        UpdateTray();
        _ipc.Start();

        if (launchAppId is not null) _ = LaunchAsync(launchAppId);

        TrimWorkingSet();

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(msg);
            DispatchMessage(msg);
        }

        Log.Write("Agent exiting");
        return 0;
    }

    // ---- window / message handling ----

    private unsafe void CreateWindow()
    {
        var hInstance = Platform.Windows.Interop.Kernel32.GetModuleHandle(null);
        fixed (char* cls = WindowClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = hInstance,
                lpszClassName = cls,
            };
            RegisterClassEx(&wc);
        }
        // A real (never shown) top-level window, so we receive TaskbarCreated / WM_ENDSESSION broadcasts.
        _hwnd = CreateWindowEx(0, WindowClass, "Lumos", 0, 0, 0, 0, 0, 0, 0, hInstance, 0);
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var self = _current;
        if (self is not null)
        {
            try
            {
                if (self.OnMessage(hwnd, msg, wParam, lParam)) return 0;
            }
            catch (Exception e)
            {
                Log.Write($"WndProc error: {e}");
            }
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private bool OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case TrayIcon.CallbackMessage:
                switch ((uint)(lParam & 0xFFFF))
                {
                    case NIN_SELECT or NIN_KEYSELECT or WM_LBUTTONDBLCLK: OpenUi(); break;
                    case WM_CONTEXTMENU or WM_RBUTTONUP: ShowMenu(); break;
                }
                return true;

            case WM_STATE_CHANGED:
                Interlocked.Exchange(ref _statePosted, 0);
                UpdateTray();
                return true;

            case WM_ENDSESSION when wParam != 0:
                // Logging off / shutting down: don't leave HDR on into the next session.
                _sessions.Dispose();
                return true;

            case WM_CLOSE:
                DestroyWindow(hwnd);
                return true;

            case WM_DESTROY:
                PostQuitMessage(0);
                return true;
        }

        if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
        {
            _tray?.Show(); // Explorer restarted
            return true;
        }
        return false;
    }

    private void OnSessionsChanged()
    {
        // Coalesce bursts of changes into one tray update on the message thread.
        if (Interlocked.Exchange(ref _statePosted, 1) == 0) PostMessage(_hwnd, WM_STATE_CHANGED, 0, 0);
    }

    private void UpdateTray()
    {
        if (_tray is null) return;
        var status = _sessions.GetStatus();
        var names = status.Sessions.Select(s => s.AppName).Distinct().ToArray();
        if (status.Paused)
            _tray.Update(TrayState.Paused, "Lumos: paused");
        else if (status.Displays.Any(d => d.OwnedByLumos && d.HdrEnabled))
            _tray.Update(TrayState.Active, names.Length > 0 ? $"Lumos: HDR on for {string.Join(", ", names)}" : "Lumos: HDR on");
        else
            _tray.Update(TrayState.Idle, names.Length > 0 ? $"Lumos: watching {string.Join(", ", names)}" : "Lumos: waiting for an HDR app");
    }

    private void ShowMenu()
    {
        var status = _sessions.GetStatus();
        var menu = CreatePopupMenu();
        try
        {
            var header = status.Paused ? "Lumos is paused"
                : status.Displays.Any(d => d.OwnedByLumos && d.HdrEnabled) ? "HDR is on" : "Waiting for an HDR app";
            AppendMenu(menu, MF_STRING | MF_GRAYED, 0, header);
            foreach (var s in status.Sessions.DistinctBy(s => s.AppId))
                AppendMenu(menu, MF_STRING | MF_GRAYED, 0, s.PendingLaunch ? $"   {s.AppName} (launching…)" : $"   {s.AppName}");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING | MF_DEFAULT, CMD_OPEN, "Open Lumos");
            AppendMenu(menu, MF_STRING | (status.Paused ? MF_CHECKED : 0), CMD_PAUSE, "Pause");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CMD_EXIT, "Exit");

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd); // required so the menu closes when clicking elsewhere
            var cmd = (uint)TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, pt.X, pt.Y, _hwnd, 0);
            switch (cmd)
            {
                case CMD_OPEN: OpenUi(); break;
                case CMD_PAUSE: SetPaused(!status.Paused); break;
                case CMD_EXIT: PostMessage(_hwnd, WM_CLOSE, 0, 0); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static void OpenUi()
    {
        var ui = Path.Combine(AppContext.BaseDirectory, "Lumos.exe");
        if (!File.Exists(ui))
        {
            Log.Write($"Settings UI not found at {ui}");
            return;
        }
        try { Process.Start(new ProcessStartInfo(ui) { UseShellExecute = false })?.Dispose(); }
        catch (Exception e) { Log.Write($"Failed to open UI: {e.Message}"); }
    }

    private void SetPaused(bool paused)
    {
        var settings = SettingsStore.Load();
        settings.Paused = paused;
        SettingsStore.Save(settings);
        _sessions.SetPaused(paused);
    }

    // ---- IPC ----

    private async Task<IpcResponse> HandleAsync(IpcRequest request)
    {
        switch (request.Command)
        {
            case IpcCommands.Status:
                return IpcResponse.Success(_sessions.GetStatus());

            case IpcCommands.Reload:
                _sessions.ApplySettings(SettingsStore.Load());
                _watcher.Rescan(); // picks up newly added apps that are already running
                return IpcResponse.Success(_sessions.GetStatus());

            case IpcCommands.SetHdr when request.DisplayId is not null:
                return _sessions.SetHdrManually(request.DisplayId, request.Enabled)
                    ? IpcResponse.Success(_sessions.GetStatus())
                    : IpcResponse.Fail("Could not change HDR on that display.");

            case IpcCommands.Launch when request.AppId is not null:
                return await LaunchAsync(request.AppId).ConfigureAwait(false);

            case IpcCommands.Pause or IpcCommands.Resume:
                SetPaused(request.Command == IpcCommands.Pause);
                return IpcResponse.Success(_sessions.GetStatus());

            case IpcCommands.Exit:
                PostMessage(_hwnd, WM_CLOSE, 0, 0);
                return IpcResponse.Success();

            default:
                return IpcResponse.Fail($"Unknown command '{request.Command}'.");
        }
    }

    /// <summary>"Launch with Lumos": HDR first, then the app, so games that check HDR only at startup see it on.</summary>
    private async Task<IpcResponse> LaunchAsync(string appId)
    {
        var app = _sessions.FindApp(appId);
        if (app is null)
        {
            _sessions.ApplySettings(SettingsStore.Load());
            app = _sessions.FindApp(appId);
        }
        if (app is null) return IpcResponse.Fail("That app is no longer in Lumos.");

        var displays = _sessions.BeginLaunch(app);
        if (displays.Length > 0)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2000 &&
                   !_hdr.GetDisplays().Where(d => displays.Contains(d.Id)).All(d => d.HdrEnabled))
                await Task.Delay(100).ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false); // let the mode change settle
        }

        try
        {
            var command = string.IsNullOrWhiteSpace(app.LaunchCommand) ? app.ExePath : app.LaunchCommand;
            var psi = new ProcessStartInfo(command)
            {
                UseShellExecute = true,
                Arguments = app.LaunchArgs ?? "",
            };
            if (File.Exists(command)) psi.WorkingDirectory = Path.GetDirectoryName(command)!;
            Process.Start(psi)?.Dispose();
            Log.Write($"Launched {app.DisplayName}");
            return IpcResponse.Success();
        }
        catch (Exception e)
        {
            Log.Write($"Launch failed for {app.DisplayName}: {e.Message}");
            _sessions.CancelLaunch(app.Id);
            return IpcResponse.Fail($"Could not start {app.DisplayName}: {e.Message}");
        }
    }

    /// <summary>Startup touches pages we never need again; hand them back to the OS once.</summary>
    private static void TrimWorkingSet()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        var proc = Platform.Windows.Interop.Kernel32.GetCurrentProcess();
        Platform.Windows.Interop.Kernel32.SetProcessWorkingSetSize(proc, -1, -1);
    }

    public void Dispose()
    {
        _ipc.Dispose();
        _watcher.Dispose();
        _sessions.StateChanged -= OnSessionsChanged;
        _sessions.Dispose(); // restores SDR on displays Lumos turned HDR on for
        _tray?.Dispose();
        if (_hwnd != 0) DestroyWindow(_hwnd);
        _current = null;
    }
}
