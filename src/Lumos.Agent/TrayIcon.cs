using System.Runtime.InteropServices;
using Lumos.Platform.Windows.Interop;

namespace Lumos.Agent;

internal enum TrayState { Idle, Active, Paused }

/// <summary>Notification-area icon via Shell_NotifyIconW. Icons are decoded once from embedded .ico resources.</summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    public const uint CallbackMessage = User32.WM_APP + 1;

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIcon(uint message, NOTIFYICONDATAW* data);

    private readonly nint _hwnd;
    private readonly nint _iconIdle, _iconActive, _iconPaused;
    private TrayState _state;
    private string _tip = "Lumos";
    private bool _added;

    public TrayIcon(nint hwnd)
    {
        _hwnd = hwnd;
        var size = User32.GetSystemMetrics(User32.SM_CXSMICON);
        _iconIdle = LoadIcon("tray-off.ico", size);
        _iconActive = LoadIcon("tray-on.ico", size);
        _iconPaused = LoadIcon("tray-paused.ico", size);
    }

    /// <summary>(Re)adds the icon. Also called after Explorer restarts ("TaskbarCreated").</summary>
    public void Show()
    {
        var data = Build(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, &data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, &data);
    }

    public void Update(TrayState state, string tip)
    {
        if (state == _state && tip == _tip) return;
        _state = state;
        _tip = tip;
        if (!_added) return;
        var data = Build(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, &data);
    }

    private NOTIFYICONDATAW Build(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _state switch { TrayState.Active => _iconActive, TrayState.Paused => _iconPaused, _ => _iconIdle },
        };
        var tip = _tip.Length > 127 ? _tip[..127] : _tip;
        tip.AsSpan().CopyTo(new Span<char>(data.szTip, 128));
        return data;
    }

    private static nint LoadIcon(string resource, int size)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(resource);
        if (stream is null) return 0;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        fixed (byte* p = bytes)
        {
            var offset = User32.LookupIconIdFromDirectoryEx(p, true, size, size, 0);
            if (offset <= 0) return 0;
            // Each ICONDIRENTRY's size is at +8 of the 16-byte entry; find the one starting at 'offset'.
            var count = *(ushort*)(p + 4);
            uint length = 0;
            for (var i = 0; i < count; i++)
            {
                var entry = p + 6 + i * 16;
                if (*(uint*)(entry + 12) == (uint)offset) { length = *(uint*)(entry + 8); break; }
            }
            return length == 0 ? 0 : User32.CreateIconFromResourceEx(p + offset, length, true, 0x00030000, size, size, 0);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Build(0);
            Shell_NotifyIcon(NIM_DELETE, &data);
            _added = false;
        }
        User32.DestroyIcon(_iconIdle);
        User32.DestroyIcon(_iconActive);
        User32.DestroyIcon(_iconPaused);
    }
}
