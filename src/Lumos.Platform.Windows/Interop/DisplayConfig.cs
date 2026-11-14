using System.Runtime.InteropServices;

namespace Lumos.Platform.Windows.Interop;

// Connecting and Configuring Displays (CCD) API. Layouts mirror wingdi.h.
internal static unsafe partial class DisplayConfig
{
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    public const uint MODE_INFO_TYPE_SOURCE = 1;

    public const uint GET_TARGET_NAME = 2;
    public const uint GET_ADVANCED_COLOR_INFO = 9;
    public const uint SET_ADVANCED_COLOR_STATE = 10;
    public const uint GET_ADVANCED_COLOR_INFO_2 = 15; // Windows 11 24H2+
    public const uint SET_HDR_STATE = 16;             // Windows 11 24H2+

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathSourceInfo
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathTargetInfo
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo
    {
        public PathSourceInfo Source;
        public PathTargetInfo Target;
        public uint Flags;
    }

    /// <summary>DISPLAYCONFIG_MODE_INFO (64 bytes). Only the source-mode fields of the union are exposed.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct ModeInfo
    {
        [FieldOffset(0)] public uint InfoType;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public LUID AdapterId;
        [FieldOffset(16)] public uint SourceWidth;
        [FieldOffset(20)] public uint SourceHeight;
        [FieldOffset(24)] public uint SourcePixelFormat;
        [FieldOffset(28)] public int SourcePositionX;
        [FieldOffset(32)] public int SourcePositionY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public LUID AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        public fixed char MonitorFriendlyDeviceName[64];
        public fixed char MonitorDevicePath[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo
    {
        public DeviceInfoHeader Header;
        public uint Value; // bit0 supported, bit1 enabled, bit2 wideColorEnforced, bit3 forceDisabled
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo2
    {
        public DeviceInfoHeader Header;
        public uint Value; // bit0 advancedColorSupported, bit1 active, bit3 limitedByPolicy, bit4 hdrSupported, bit5 hdrUserEnabled, bit6 wcgSupported, bit7 wcgUserEnabled
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode; // 0 SDR, 1 WCG, 2 HDR
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SetState
    {
        public DeviceInfoHeader Header;
        public uint Value; // bit0 enable
    }

    [LibraryImport("user32.dll")]
    public static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [LibraryImport("user32.dll")]
    public static partial int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, PathInfo* pathArray,
        ref uint numModeInfoArrayElements, ModeInfo* modeInfoArray, nint currentTopologyId);

    [LibraryImport("user32.dll")]
    public static partial int DisplayConfigGetDeviceInfo(void* requestPacket);

    [LibraryImport("user32.dll")]
    public static partial int DisplayConfigSetDeviceInfo(void* setPacket);
}
