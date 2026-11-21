namespace Lumos.Core.Games;

/// <summary>Finds games in a folder the user added in Settings, e.g. D:\Games.</summary>
public static class CustomFolderScanner
{
    /// <summary>
    /// Each subfolder holding a plausible game exe is one game, named after the subfolder.
    /// If the folder itself has game exes at its top level, it's treated as a single game folder instead.
    /// </summary>
    public static List<InstalledGame> Scan(string root)
    {
        var games = new List<InstalledGame>();
        if (!Directory.Exists(root)) return games;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        var options = new EnumerationOptions { IgnoreInaccessible = true };
        try
        {
            if (Directory.EnumerateFiles(root, "*.exe", options).Any(f => !GameExeResolver.IsNonGameExe(f)))
            {
                games.Add(new InstalledGame(FolderName(root), GameSources.Folder, root));
                return games;
            }

            foreach (var dir in Directory.EnumerateDirectories(root, "*", options))
                if (GameExeResolver.ContainsGameExe(dir))
                    games.Add(new InstalledGame(FolderName(dir), GameSources.Folder, dir));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return games;
    }

    private static string FolderName(string dir)
    {
        var name = Path.GetFileName(dir);
        return string.IsNullOrEmpty(name) ? dir : name;
    }
}
