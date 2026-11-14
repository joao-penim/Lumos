using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Lumos.Core;

namespace Lumos.Platform.Windows;

/// <summary>Creates .lnk files through IShellLinkW (source-generated COM, trimming/AOT safe).</summary>
public sealed partial class WindowsShortcutService(string agentPath) : IShortcutService
{
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    public string CreateDesktopShortcut(WatchedApp app) =>
        CreateShortcut(app, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    public string CreateShortcut(WatchedApp app, string folder)
    {
        var name = string.Concat((app.DisplayName.Length > 0 ? app.DisplayName : app.ExeFileName).Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(folder, $"{name} (HDR).lnk");

        var hr = CoCreateInstance(CLSID_ShellLink, 0, 1 /* CLSCTX_INPROC_SERVER */, IID_IShellLinkW, out var ptr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var comWrappers = new StrategyBasedComWrappers();
            var obj = comWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
            var link = (IShellLinkW)obj;
            link.SetPath(agentPath);
            link.SetArguments($"--launch {app.Id}");
            link.SetWorkingDirectory(Path.GetDirectoryName(app.ExePath) ?? "");
            link.SetDescription($"Launch {app.DisplayName} with HDR (Lumos)");
            link.SetIconLocation(app.ExePath, 0);
            ((IPersistFile)obj).Save(path, true);
        }
        finally
        {
            Marshal.Release(ptr);
        }
        return path;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint ppv);

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal partial interface IShellLinkW
    {
        void GetPath(nint pszFile, int cch, nint pfd, uint fFlags);
        void GetIDList(out nint ppidl);
        void SetIDList(nint pidl);
        void GetDescription(nint pszName, int cch);
        void SetDescription(string pszName);
        void GetWorkingDirectory(nint pszDir, int cch);
        void SetWorkingDirectory(string pszDir);
        void GetArguments(nint pszArgs, int cch);
        void SetArguments(string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(nint pszIconPath, int cch, out int piIcon);
        void SetIconLocation(string pszIconPath, int iIcon);
        void SetRelativePath(string pszPathRel, uint dwReserved);
        void Resolve(nint hwnd, uint fFlags);
        void SetPath(string pszFile);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    internal partial interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load(string pszFileName, uint dwMode);
        void Save(string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted(string pszFileName);
        void GetCurFile(out nint ppszFileName);
    }
}
