using Lumos.Core;
using Microsoft.Win32;

namespace Lumos.Platform.Windows;

/// <summary>Per-user autostart via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
public sealed class RegistryAutostart(string agentPath) : IAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumos";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string v && v.Contains(Path.GetFileName(agentPath), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{agentPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
