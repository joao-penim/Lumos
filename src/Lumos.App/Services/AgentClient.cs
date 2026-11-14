using System.Diagnostics;
using Lumos.Core;

namespace Lumos.App.Services;

/// <summary>Talks to LumosAgent.exe over the named pipe, starting it if needed.</summary>
public sealed class AgentClient
{
    public static string AgentPath { get; } = Path.Combine(AppContext.BaseDirectory, "LumosAgent.exe");

    public Task<IpcResponse?> SendAsync(string command, string? appId = null, string? displayId = null, bool enabled = false) =>
        IpcClient.SendAsync(new IpcRequest { Command = command, AppId = appId, DisplayId = displayId, Enabled = enabled }, timeoutMs: 1500);

    public async Task<AgentStatus?> GetStatusAsync() => (await SendAsync(IpcCommands.Status).ConfigureAwait(false))?.Status;

    public async Task<bool> EnsureRunningAsync()
    {
        if (await GetStatusAsync().ConfigureAwait(false) is not null) return true;
        if (!File.Exists(AgentPath)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(AgentPath, "--minimized") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception)
        {
            return false;
        }

        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(150).ConfigureAwait(false);
            if (await GetStatusAsync().ConfigureAwait(false) is not null) return true;
        }
        return false;
    }
}
