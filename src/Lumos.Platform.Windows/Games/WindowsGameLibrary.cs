using Lumos.Core;
using Lumos.Core.Games;
using Microsoft.Win32;

namespace Lumos.Platform.Windows.Games;

/// <summary>
/// Finds installed games from Steam, the EA app, Epic Games and GOG, plus the user's own game folders.
/// Used by the settings UI on demand only; the agent never calls it.
/// </summary>
public static class WindowsGameLibrary
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// All games found, deduplicated by install folder (launchers win over custom folders) and sorted by name.
    /// A launcher that fails to read is skipped, so one broken source never empties the list.
    /// </summary>
    public static List<InstalledGame> FindInstalledGames(IEnumerable<string> customFolders)
    {
        var all = new List<InstalledGame>();
        Collect(all, "Steam", FindSteam);
        Collect(all, "Epic", FindEpic);
        Collect(all, "GOG", FindGog);
        Collect(all, "EA", FindEa);
        foreach (var folder in customFolders)
            Collect(all, $"folder {folder}", () => CustomFolderScanner.Scan(folder));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. all.Where(g => seen.Add(Path.TrimEndingDirectorySeparator(g.InstallDir)))
                      .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static void Collect(List<InstalledGame> into, string source, Func<IEnumerable<InstalledGame>> find)
    {
        try { into.AddRange(find()); }
        catch (Exception e) { Log.Write($"Game scan: {source} failed: {e.Message}"); }
    }

    private static IEnumerable<InstalledGame> FindSteam()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(path))
        {
            using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Valve\Steam");
            path = lm?.GetValue("InstallPath") as string;
        }
        return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ? [] : SteamLibrary.FindGames(Path.GetFullPath(path));
    }

    private static IEnumerable<InstalledGame> FindEpic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        return EpicManifest.FindGames(dir);
    }

    private static IEnumerable<InstalledGame> FindGog()
    {
        using var games = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (games is null) yield break;
        foreach (var id in games.GetSubKeyNames())
        {
            using var g = games.OpenSubKey(id);
            if (g is null) continue;
            var name = g.GetValue("gameName") as string;
            var dir = g.GetValue("path") as string;
            var exe = g.GetValue("exe") as string;
            if (!string.IsNullOrEmpty(g.GetValue("dependsOn") as string)) continue; // DLC
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            if (!string.IsNullOrWhiteSpace(exe) && !Path.IsPathFullyQualified(exe)) exe = Path.Combine(dir, exe);
            yield return new InstalledGame(name.Trim(), GameSources.Gog, dir, string.IsNullOrWhiteSpace(exe) ? null : exe);
        }
    }

    private static IEnumerable<InstalledGame> FindEa()
    {
        var dirs = new List<string>();

        // Every EA app game registers an uninstall entry pointing at its install folder.
        foreach (var view in (RegistryView[])[RegistryView.Registry64, RegistryView.Registry32])
        {
            using var uninstall = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(UninstallKey);
            if (uninstall is null) continue;
            foreach (var sub in uninstall.GetSubKeyNames())
            {
                using var k = uninstall.OpenSubKey(sub);
                if (k?.GetValue("InstallLocation") is string loc && !string.IsNullOrWhiteSpace(loc)) dirs.Add(loc.Trim().Trim('"'));
            }
        }

        // The EA app's download folder (user_<id>.ini) and the default one.
        var eaDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Electronic Arts", "EA Desktop");
        var roots = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games") };
        if (Directory.Exists(eaDesktop))
        {
            foreach (var ini in Directory.EnumerateFiles(eaDesktop, "user_*.ini"))
            {
                try
                {
                    const string prefix = "user.downloadinplacedir=";
                    foreach (var line in File.ReadLines(ini))
                        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) roots.Add(line[prefix.Length..].Trim());
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        foreach (var root in roots.Where(Directory.Exists))
            dirs.AddRange(Directory.EnumerateDirectories(root));

        return EaInstallerData.FindGames(dirs.Where(d => File.Exists(Path.Combine(d, EaInstallerData.RelativePath))));
    }
}
