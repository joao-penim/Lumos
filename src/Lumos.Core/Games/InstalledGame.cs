namespace Lumos.Core.Games;

/// <summary>A game found in a launcher library or a custom game folder.</summary>
/// <param name="Name">Display name, as the launcher shows it.</param>
/// <param name="Source">Where it was found: "Steam", "EA app", "Epic Games", "GOG" or "Folder".</param>
/// <param name="InstallDir">The game's install folder.</param>
/// <param name="ExePath">The executable the launcher names, if it names one. Use <see cref="GameExeResolver.Resolve"/> to pick the exe to watch.</param>
/// <param name="LaunchCommand">How the launcher starts the game, e.g. steam://rungameid/730, or null to start the exe.</param>
public sealed record InstalledGame(string Name, string Source, string InstallDir, string? ExePath = null, string? LaunchCommand = null)
{
    /// <summary>True when <paramref name="path"/> is inside <see cref="InstallDir"/>.</summary>
    public bool Contains(string path)
    {
        var dir = Path.TrimEndingDirectorySeparator(InstallDir);
        return path.Length > dir.Length + 1
               && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
               && (path[dir.Length] == Path.DirectorySeparatorChar || path[dir.Length] == Path.AltDirectorySeparatorChar);
    }
}

public static class GameSources
{
    public const string Steam = "Steam";
    public const string Ea = "EA app";
    public const string Epic = "Epic Games";
    public const string Gog = "GOG";
    public const string Folder = "Folder";
}
