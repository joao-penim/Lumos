namespace Lumos.Core;

public static class DisplayTargetResolver
{
    /// <summary>Returns the ids of the HDR-capable displays an app should switch.</summary>
    public static string[] Resolve(WatchedApp app, LumosSettings settings, IReadOnlyList<DisplayInfo> displays)
    {
        var kind = app.Target;
        var specificId = app.TargetDisplayId;
        if (kind == DisplayTargetKind.Default)
        {
            kind = settings.DefaultTarget;
            specificId = settings.DefaultDisplayId;
        }

        switch (kind)
        {
            case DisplayTargetKind.AllHdrCapable:
                return [.. displays.Where(d => d.HdrSupported).Select(d => d.Id)];

            case DisplayTargetKind.Specific:
                var match = displays.FirstOrDefault(d => string.Equals(d.Id, specificId, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match.HdrSupported ? [match.Id] : [];
                // Chosen monitor not connected: fall back to the primary display.
                goto default;

            default: // Primary (Default is resolved above and can't reach here)
                var primary = displays.FirstOrDefault(d => d.IsPrimary);
                return primary is { HdrSupported: true } ? [primary.Id] : [];
        }
    }
}
