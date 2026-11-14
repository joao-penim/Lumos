using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lumos.Platform.Windows;

namespace Lumos.App.Services;

public static class IconLoader
{
    public static Task<Bitmap?> LoadAsync(string exePath) => Task.Run(() => Load(exePath));

    public static Bitmap? Load(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;
        var icon = WindowsShell.ExtractIcon(exePath);
        if (icon is not { } i) return null;

        var handle = GCHandle.Alloc(i.Bgra, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                new PixelSize(i.Width, i.Height), new Vector(96, 96), i.Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }
}
