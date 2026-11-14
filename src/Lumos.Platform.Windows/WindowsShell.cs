using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumos.Platform.Windows.Interop;

namespace Lumos.Platform.Windows;

public sealed record RunningApp(string Name, string ExePath, string? WindowTitle);

/// <summary>Shell helpers used by the settings UI only (never loaded by the agent's hot paths).</summary>
public static unsafe partial class WindowsShell
{
    /// <summary>Extracts an exe's icon as straight (non-premultiplied) BGRA pixels, or null.</summary>
    public static (byte[] Bgra, int Width, int Height)? ExtractIcon(string exePath, int size = 48)
    {
        nint hicon = 0;
        uint id;
        if (PrivateExtractIconsW(exePath, 0, size, size, &hicon, &id, 1, 0) != 1 || hicon == 0) return null;
        try { return IconToBgra(hicon); }
        finally { User32.DestroyIcon(hicon); }
    }

    /// <summary>Running processes that own a visible main window, deduplicated by path.</summary>
    public static List<RunningApp> GetRunningWindowedApps()
    {
        var result = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == 0) continue;
                    var path = Kernel32.GetProcessPath((uint)p.Id);
                    if (path is null || result.ContainsKey(path)) continue;
                    if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) continue;
                    result[path] = new RunningApp(Path.GetFileNameWithoutExtension(path), path, p.MainWindowTitle);
                }
                catch (InvalidOperationException) { } // exited meanwhile
            }
        }
        return [.. result.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Friendly product name from the exe's version resource, falling back to the file name.</summary>
    public static string GetFriendlyName(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch (FileNotFoundException) { }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    private static (byte[], int, int)? IconToBgra(nint hicon)
    {
        if (!GetIconInfo(hicon, out var info)) return null;
        try
        {
            BITMAP bmp;
            if (info.hbmColor == 0 || GetObjectW(info.hbmColor, sizeof(BITMAP), &bmp) == 0) return null;
            int w = bmp.bmWidth, h = bmp.bmHeight;
            var pixels = new byte[w * h * 4];
            var bi = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
            };
            var dc = GetDC(0);
            try
            {
                fixed (byte* p = pixels)
                    if (GetDIBits(dc, info.hbmColor, 0, (uint)h, p, &bi, 0) == 0) return null;

                // Legacy icons have no alpha channel: derive it from the AND mask.
                var hasAlpha = false;
                for (var i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
                if (!hasAlpha && info.hbmMask != 0)
                {
                    var mask = new byte[w * h * 4];
                    fixed (byte* m = mask)
                        GetDIBits(dc, info.hbmMask, 0, (uint)h, m, &bi, 0);
                    for (var i = 0; i < pixels.Length; i += 4) pixels[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
                }
            }
            finally { ReleaseDC(0, dc); }
            return (pixels, w, h);
        }
        finally
        {
            if (info.hbmColor != 0) DeleteObject(info.hbmColor);
            if (info.hbmMask != 0) DeleteObject(info.hbmMask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        public uint bmiColors; // one RGBQUAD slot so the struct is a valid BITMAPINFO
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, nint* icons, uint* ids, uint count, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out ICONINFO info);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint obj, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint hdc, nint bitmap, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);
}
