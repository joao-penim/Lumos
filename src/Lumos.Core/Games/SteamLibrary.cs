namespace Lumos.Core.Games;

/// <summary>Reads Steam's library folders and app manifests. Works on any OS given the Steam install folder.</summary>
public static class SteamLibrary
{
    public sealed record SteamApp(string AppId, string Name, string InstallDir);

    private const int StateFullyInstalled = 4;

    /// <summary>Steam ships these as "apps", but they aren't games.</summary>
    private static readonly HashSet<string> ToolAppIds = ["228980"]; // Steamworks Common Redistributables

    private static readonly string[] ToolNamePrefixes = ["Steamworks Common", "Steam Linux Runtime", "Proton "];

    /// <summary>The library folder paths listed in libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText)
    {
        var root = VdfObject.Parse(vdfText).GetObject("libraryfolders");
        if (root is null) return [];
        return [.. root.Children.Select(c => c.GetString("path")).OfType<string>().Where(p => p.Length > 0)];
    }

    /// <summary>The game in an appmanifest_*.acf, or null for tools, redistributables and unfinished installs.</summary>
    public static SteamApp? ParseAppManifest(string acfText)
    {
        var app = VdfObject.Parse(acfText).GetObject("AppState");
        if (app is null) return null;
        var id = app.GetString("appid");
        var name = app.GetString("name");
        var dir = app.GetString("installdir");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir)) return null;
        if (ToolAppIds.Contains(id) || ToolNamePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return null;
        if (int.TryParse(app.GetString("StateFlags"), out var flags) && (flags & StateFullyInstalled) == 0) return null;
        return new SteamApp(id, name, dir);
    }

    /// <summary>Installed games across all libraries of the Steam install at <paramref name="steamPath"/>.</summary>
    public static List<InstalledGame> FindGames(string steamPath)
    {
        var games = new List<InstalledGame>();
        var main = Path.Combine(steamPath, "steamapps");
        // libraryfolders.vdf lists the main library too, with proper casing (the registry's SteamPath is lowercase), so it goes first.
        var libraries = new List<string>();
        var vdf = Path.Combine(main, "libraryfolders.vdf");
        if (File.Exists(vdf)) libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        libraries.Add(steamPath);

        foreach (var library in libraries.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                SteamApp? app;
                try { app = ParseAppManifest(File.ReadAllText(manifest)); }
                catch (IOException) { continue; }
                if (app is null) continue;

                var dir = Path.Combine(apps, "common", app.InstallDir);
                if (Directory.Exists(dir))
                    games.Add(new InstalledGame(app.Name, GameSources.Steam, dir, LaunchCommand: $"steam://rungameid/{app.AppId}"));
            }
        }
        return games;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
