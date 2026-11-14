using Lumos.Core;

namespace Lumos.Core.Tests;

internal sealed class FakeHdr : IHdrController
{
    public readonly Dictionary<string, DisplayInfo> Displays = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<(string Id, bool Enabled)> Calls = [];

    public FakeHdr Add(string id, bool primary = false, bool supported = true, bool enabled = false)
    {
        Displays[id] = new DisplayInfo(id, id, primary, supported, enabled);
        return this;
    }

    public bool IsOn(string id) => Displays[id].HdrEnabled;

    /// <summary>Simulates the user pressing Win+Alt+B.</summary>
    public void UserSet(string id, bool enabled) => Displays[id] = Displays[id] with { HdrEnabled = enabled };

    public IReadOnlyList<DisplayInfo> GetDisplays() => [.. Displays.Values];

    public bool SetHdr(string displayId, bool enabled)
    {
        if (!Displays.TryGetValue(displayId, out var d) || !d.HdrSupported) return false;
        Calls.Add((displayId, enabled));
        Displays[displayId] = d with { HdrEnabled = enabled };
        return true;
    }
}

internal sealed class FakeTimers : ITimerFactory
{
    private readonly List<Entry> _entries = [];
    private TimeSpan _now;

    private sealed class Entry(TimeSpan due, Action callback, FakeTimers owner) : IDisposable
    {
        public TimeSpan Due { get; } = due;
        public Action Callback { get; } = callback;
        public void Dispose() => owner._entries.Remove(this);
    }

    public int Pending => _entries.Count;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var e = new Entry(_now + delay, callback, this);
        _entries.Add(e);
        return e;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var e in _entries.Where(e => e.Due <= _now).ToArray())
        {
            if (!_entries.Remove(e)) continue;
            e.Callback();
        }
    }
}

internal sealed class FakeState : IStateStore
{
    public List<string> Owned = [];
    public IReadOnlyCollection<string> LoadOwnedDisplays() => [.. Owned];
    public void SaveOwnedDisplays(IReadOnlyCollection<string> displayIds) => Owned = [.. displayIds];
}
