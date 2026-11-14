using Lumos.Core;
using static Lumos.Platform.Windows.Interop.DisplayConfig;

namespace Lumos.Platform.Windows;

/// <summary>Reads and toggles HDR through the CCD API, the same mechanism Win+Alt+B and Settings use.</summary>
public sealed unsafe class WindowsHdrController : IHdrController
{
    // Windows 11 24H2 (build 26100) split "advanced color" into separate HDR / WCG states.
    private static readonly bool UseHdrStateApi = Environment.OSVersion.Version.Build >= 26100;

    /// <summary>Return codes of the last <see cref="SetHdr"/> call, for the --hdr diagnostics command.</summary>
    public string LastDiagnostic { get; private set; } = "";

    private readonly record struct Target(LUID AdapterId, uint TargetId, DisplayInfo Info);

    public IReadOnlyList<DisplayInfo> GetDisplays() => [.. QueryTargets().Select(t => t.Info)];

    public bool SetHdr(string displayId, bool enabled)
    {
        foreach (var t in QueryTargets())
        {
            if (!string.Equals(t.Info.Id, displayId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!t.Info.HdrSupported) return false;

            var packet = new SetState
            {
                Header = new DeviceInfoHeader
                {
                    Type = UseHdrStateApi ? SET_HDR_STATE : SET_ADVANCED_COLOR_STATE,
                    Size = (uint)sizeof(SetState),
                    AdapterId = t.AdapterId,
                    Id = t.TargetId,
                },
                Value = enabled ? 1u : 0u,
            };
            var rc = DisplayConfigSetDeviceInfo(&packet);
            LastDiagnostic = $"type {packet.Header.Type} rc={rc}";
            if (rc != ERROR_SUCCESS && UseHdrStateApi)
            {
                // Some drivers reject the newer packet; the legacy one still works there.
                packet.Header.Type = SET_ADVANCED_COLOR_STATE;
                rc = DisplayConfigSetDeviceInfo(&packet);
                LastDiagnostic += $"; type {packet.Header.Type} rc={rc}";
            }
            if (rc == ERROR_SUCCESS) return true;

            // Observed on real hardware: the call can report an error while the switch still happens.
            // Trust the display's actual state over the return code.
            for (var i = 0; i < 5; i++)
            {
                Thread.Sleep(100);
                if (GetHdrState(t.AdapterId, t.TargetId).Enabled == enabled) return true;
            }
            return false;
        }
        return false;
    }

    private static List<Target> QueryTargets()
    {
        PathInfo[] paths;
        ModeInfo[] modes;
        uint pathCount, modeCount;
        int rc;
        do
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != ERROR_SUCCESS) return [];
            paths = new PathInfo[pathCount];
            modes = new ModeInfo[modeCount];
            fixed (PathInfo* pp = paths)
            fixed (ModeInfo* pm = modes)
                rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, pp, ref modeCount, pm, 0);
        } while (rc == ERROR_INSUFFICIENT_BUFFER); // topology changed between the two calls
        if (rc != ERROR_SUCCESS) return [];

        var result = new List<Target>((int)pathCount);
        for (var i = 0; i < pathCount; i++)
        {
            ref readonly var path = ref paths[i];
            var adapter = path.Target.AdapterId;
            var targetId = path.Target.Id;

            // Mirrored (cloned) outputs share a target; report each target once.
            if (result.Exists(r => r.TargetId == targetId && r.AdapterId.LowPart == adapter.LowPart && r.AdapterId.HighPart == adapter.HighPart))
                continue;

            var (name, devicePath) = GetTargetName(adapter, targetId);
            var (supported, enabled) = GetHdrState(adapter, targetId);

            var srcIdx = path.Source.ModeInfoIdx;
            var isPrimary = srcIdx < modeCount
                && modes[srcIdx].InfoType == MODE_INFO_TYPE_SOURCE
                && modes[srcIdx].SourcePositionX == 0
                && modes[srcIdx].SourcePositionY == 0;

            var id = string.IsNullOrEmpty(devicePath)
                ? $"{adapter.HighPart:X8}{adapter.LowPart:X8}:{targetId}"
                : devicePath;
            if (string.IsNullOrEmpty(name)) name = isPrimary ? "Primary display" : $"Display {i + 1}";

            result.Add(new Target(adapter, targetId, new DisplayInfo(id, name, isPrimary, supported, enabled)));
        }
        return result;
    }

    private static (string Name, string DevicePath) GetTargetName(LUID adapter, uint targetId)
    {
        var req = new TargetDeviceName
        {
            Header = new DeviceInfoHeader { Type = GET_TARGET_NAME, Size = (uint)sizeof(TargetDeviceName), AdapterId = adapter, Id = targetId },
        };
        if (DisplayConfigGetDeviceInfo(&req) != ERROR_SUCCESS) return ("", "");
        return (new string(req.MonitorFriendlyDeviceName), new string(req.MonitorDevicePath));
    }

    private static (bool Supported, bool Enabled) GetHdrState(LUID adapter, uint targetId)
    {
        if (UseHdrStateApi)
        {
            var req2 = new AdvancedColorInfo2
            {
                Header = new DeviceInfoHeader { Type = GET_ADVANCED_COLOR_INFO_2, Size = (uint)sizeof(AdvancedColorInfo2), AdapterId = adapter, Id = targetId },
            };
            if (DisplayConfigGetDeviceInfo(&req2) == ERROR_SUCCESS)
            {
                var hdrSupported = (req2.Value & (1u << 4)) != 0;
                var hdrEnabled = (req2.Value & (1u << 5)) != 0 || req2.ActiveColorMode == 2;
                return (hdrSupported, hdrEnabled);
            }
        }

        var req = new AdvancedColorInfo
        {
            Header = new DeviceInfoHeader { Type = GET_ADVANCED_COLOR_INFO, Size = (uint)sizeof(AdvancedColorInfo), AdapterId = adapter, Id = targetId },
        };
        if (DisplayConfigGetDeviceInfo(&req) != ERROR_SUCCESS) return (false, false);
        return ((req.Value & 1u) != 0, (req.Value & 2u) != 0);
    }
}
