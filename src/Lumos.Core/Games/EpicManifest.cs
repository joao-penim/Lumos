using System.Text.Json;

namespace Lumos.Core.Games;

/// <summary>Reads the Epic Games Launcher's per-game *.item manifests (JSON).</summary>
public static class EpicManifest
{
    /// <summary>The game described by one .item file, or null for DLC, apps that aren't games, and unfinished installs.</summary>
    public static InstalledGame? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var name = Str(root, "DisplayName");
        var dir = Str(root, "InstallLocation");
        var exe = Str(root, "LaunchExecutable");
        var appName = Str(root, "AppName");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(appName)) return null;

        if (Bool(root, "bIsIncompleteInstall")) return null;
        var mainApp = Str(root, "MainGameAppName");
        if (!string.IsNullOrEmpty(mainApp) && !string.Equals(mainApp, appName, StringComparison.OrdinalIgnoreCase)) return null; // DLC
        if (root.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array &&
            !cats.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "games"))
            return null;

        var exePath = string.IsNullOrWhiteSpace(exe) ? null : Path.GetFullPath(Path.Combine(dir, exe));
        return new InstalledGame(name, GameSources.Epic, dir, exePath,
            $"com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch&silent=true");
    }

    /// <summary>Installed games from every manifest in <paramref name="manifestDir"/>.</summary>
    public static List<InstalledGame> FindGames(string manifestDir)
    {
        var games = new List<InstalledGame>();
        if (!Directory.Exists(manifestDir)) return games;
        foreach (var file in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            try
            {
                if (Parse(File.ReadAllText(file)) is { } game && Directory.Exists(game.InstallDir)) games.Add(game);
            }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        return games;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
