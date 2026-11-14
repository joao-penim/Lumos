using Lumos.Core;
using Lumos.Platform.Windows;
using Lumos.Platform.Windows.Interop;

namespace Lumos.Agent;

internal static class Program
{
    private const string MutexName = @"Local\Lumos.Agent";

    /// <summary>
    /// LumosAgent.exe                  start the background agent (or open the UI if it's already running)
    /// LumosAgent.exe --launch &lt;id&gt;    turn HDR on, then start the app (used by desktop shortcuts)
    /// LumosAgent.exe --exit           stop the running agent
    /// LumosAgent.exe --hdr list|on|off [displayId]   diagnostics, prints to the calling console
    /// </summary>
    private static int Main(string[] args)
    {
        var launchId = ArgValue(args, "--launch");

        if (args.Length > 0 && args[0] == "--hdr") return HdrCommand(args);

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Already running: forward the request to the existing instance.
            var request = launchId is not null ? new IpcRequest { Command = IpcCommands.Launch, AppId = launchId }
                : args.Contains("--exit") ? new IpcRequest { Command = IpcCommands.Exit }
                : null;
            if (request is not null) IpcClient.SendAsync(request, timeoutMs: 5000).GetAwaiter().GetResult();
            else if (!args.Contains("--minimized")) OpenUi();
            return 0;
        }
        if (args.Contains("--exit")) return 0;

        Log.Initialize(LumosPaths.LogFile);
        using var host = new AgentHost();
        return host.Run(launchId);
    }

    private static void OpenUi()
    {
        var ui = Path.Combine(AppContext.BaseDirectory, "Lumos.exe");
        if (File.Exists(ui)) System.Diagnostics.Process.Start(ui)?.Dispose();
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int HdrCommand(string[] args)
    {
        Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS);
        var hdr = new WindowsHdrController();
        var displays = hdr.GetDisplays();
        var verb = args.Length > 1 ? args[1] : "list";

        if (verb == "list")
        {
            Console.WriteLine();
            foreach (var d in displays)
                Console.WriteLine($"{(d.IsPrimary ? "*" : " ")} {d.FriendlyName,-24} HDR supported={d.HdrSupported,-5} enabled={d.HdrEnabled,-5} id={d.Id}");
            return 0;
        }

        var target = args.Length > 2
            ? displays.FirstOrDefault(d => d.Id.Equals(args[2], StringComparison.OrdinalIgnoreCase))
            : displays.FirstOrDefault(d => d.IsPrimary);
        if (target is null)
        {
            Console.WriteLine("\nDisplay not found.");
            return 1;
        }
        var ok = hdr.SetHdr(target.Id, verb == "on");
        Console.WriteLine($"\nHDR {verb} on {target.FriendlyName}: {(ok ? "OK" : "FAILED")} ({hdr.LastDiagnostic})");
        return ok ? 0 : 1;
    }
}
