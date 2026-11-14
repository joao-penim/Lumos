namespace Lumos.Core;

/// <summary>Reads and switches the HDR state of displays.</summary>
public interface IHdrController
{
    /// <summary>Queries the currently active displays. Called only on events, never on a loop.</summary>
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>Turns HDR on or off for one display. Returns false if the platform call failed.</summary>
    bool SetHdr(string displayId, bool enabled);
}

/// <summary>One-shot timers, abstracted so the session logic can be tested deterministically.</summary>
public interface ITimerFactory
{
    IDisposable Schedule(TimeSpan delay, Action callback);
}

/// <summary>Persists which displays Lumos turned HDR on for, so a crash can be recovered from.</summary>
public interface IStateStore
{
    IReadOnlyCollection<string> LoadOwnedDisplays();
    void SaveOwnedDisplays(IReadOnlyCollection<string> displayIds);
}

/// <summary>Registers the agent to start at login.</summary>
public interface IAutostart
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

/// <summary>Creates "Launch with Lumos" shortcuts.</summary>
public interface IShortcutService
{
    /// <summary>Creates a desktop shortcut that launches <paramref name="app"/> through the agent. Returns the shortcut path.</summary>
    string CreateDesktopShortcut(WatchedApp app);
}

public sealed class ThreadPoolTimerFactory : ITimerFactory
{
    public static readonly ThreadPoolTimerFactory Instance = new();

    public IDisposable Schedule(TimeSpan delay, Action callback) =>
        new Timer(static s => ((Action)s!)(), callback, delay, Timeout.InfiniteTimeSpan);
}
