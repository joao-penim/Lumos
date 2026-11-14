using System.Runtime.InteropServices;
using Avalonia;

namespace Lumos.App;

internal static partial class Program
{
    public const string WindowTitle = "Lumos";

    [STAThread]
    public static void Main(string[] args)
    {
        // One settings window at a time: a second launch just brings the first to the front.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Lumos.UI", out var createdNew);
        if (!createdNew)
        {
            var hwnd = FindWindowW(null, WindowTitle);
            if (hwnd != 0)
            {
                ShowWindow(hwnd, 9 /* SW_RESTORE */);
                SetForegroundWindow(hwnd);
            }
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string? className, string windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int cmd);
}
