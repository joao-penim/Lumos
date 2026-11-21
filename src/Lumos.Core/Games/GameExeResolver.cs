using System.Text;
using System.Text.RegularExpressions;

namespace Lumos.Core.Games;

/// <summary>
/// Guesses which executable in a game folder is the game itself, i.e. the process that owns the game window.
/// Launchers rarely say so: Steam names no exe, and EA names an anti-cheat bootstrapper.
/// </summary>
public static partial class GameExeResolver
{
    private const int MaxDepth = 4;
    private const int MaxDirectories = 3000;
    public const int MaxCandidates = 8;

    private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "_CommonRedist", "CommonRedist", "Redist", "Redistributables", "redistributable", "vcredist", "DirectX", "DotNet",
        "Prereqs", "Prerequisites", "Installers", "Support", "__Installer", "EasyAntiCheat", "EAAntiCheat", "BattlEye",
        "Engine", "CrashReporter", "ThirdParty", "Tools",
    };

    private static readonly string[] PenaltyWords =
        ["launcher", "bootstrap", "trial", "showcase", "demo", "benchmark", "config", "settings", "server", "editor", "tool", "console", "test"];

    /// <summary>Exe names that are never the game: installers, redistributables, crash reporters, anti-cheat services, helpers.</summary>
    [GeneratedRegex(@"^(unins.*|setup.*|.*redist.*|dxsetup|dotnetfx.*|ndp\d.*|.*crash.*|.*report.*|ue\dprereqsetup.*|.*prereq.*|unitycrashhandler.*|" +
                    @"start_protected_game|eaanticheat.*|easyanticheat.*|beservice.*|.*eac(64)?|.*_be|.*helper.*|.*updater.*|.*installer.*|" +
                    @"vconsole\d*|.*cefsubprocess.*|.*webhelper.*|touchup|cleanup|.*_host|winmtr|.*service)$", RegexOptions.IgnoreCase)]
    private static partial Regex SkippedExeName();

    [GeneratedRegex(@"-win(64|gdk)-shipping$", RegexOptions.IgnoreCase)]
    private static partial Regex ShippingSuffix();

    /// <summary>True for exes that are never the game (installers, crash reporters, anti-cheat, ...).</summary>
    public static bool IsNonGameExe(string path) => SkippedExeName().IsMatch(Path.GetFileNameWithoutExtension(path));

    /// <summary>
    /// The exes worth watching for <paramref name="game"/>, best first, at most <see cref="MaxCandidates"/>.
    /// The exe the launcher names comes first when it's a real game exe.
    /// </summary>
    public static IReadOnlyList<string> Resolve(InstalledGame game)
    {
        var ranked = Rank(game.InstallDir, game.Name);
        if (game.ExePath is { } named && File.Exists(named) && !IsNonGameExe(named))
            ranked = [named, .. ranked.Where(p => !string.Equals(p, named, StringComparison.OrdinalIgnoreCase))];
        return ranked.Count > MaxCandidates ? ranked.GetRange(0, MaxCandidates) : ranked;
    }

    /// <summary>Every plausible exe under <paramref name="installDir"/>, best first.</summary>
    public static List<string> Rank(string installDir, string gameName)
    {
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(installDir));
        var gameNorm = Normalize(gameName);
        var scored = new List<(string Path, double Score)>();
        foreach (var (path, depth) in EnumerateExes(installDir))
        {
            long size;
            try { size = new FileInfo(path).Length; }
            catch (IOException) { continue; }
            scored.Add((path, Score(Path.GetRelativePath(installDir, path), depth, size, gameName, folderName, gameNorm)));
        }
        return [.. scored.OrderByDescending(s => s.Score).ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase).Select(s => s.Path)];
    }

    /// <summary>True when the folder holds at least one plausible game exe.</summary>
    public static bool ContainsGameExe(string dir) => EnumerateExes(dir).Any();

    internal static double Score(string relativePath, int depth, long sizeBytes, string gameName, string folderName, string? gameNorm = null)
    {
        gameNorm ??= Normalize(gameName);
        var stem = Path.GetFileNameWithoutExtension(relativePath);
        var score = 0.0;

        // Unreal: Game.exe is a bootstrapper; Game\Binaries\Win64\Game-Win64-Shipping.exe owns the window.
        var shipping = ShippingSuffix().Match(stem);
        if (shipping.Success)
        {
            score += 60;
            stem = stem[..shipping.Index];
        }

        var n = Normalize(stem);
        score += Math.Max(NameMatch(n, gameName), NameMatch(n, folderName));

        var rel = relativePath.Replace('/', '\\');
        if (rel.Contains(@"\Binaries\Win64\", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith(@"Binaries\Win64\", StringComparison.OrdinalIgnoreCase))
            score += 15;

        foreach (var word in PenaltyWords)
            if (n.Contains(word, StringComparison.Ordinal) && !gameNorm.Contains(word, StringComparison.Ordinal))
                score -= 15;

        score -= 3 * depth;
        score += Math.Min(Math.Log2(sizeBytes / (1024.0 * 1024.0) + 1) * 3, 30);
        return score;
    }

    private static double NameMatch(string exeNorm, string target)
    {
        var t = Normalize(target);
        if (exeNorm.Length == 0 || t.Length == 0) return 0;
        if (exeNorm == t) return 50;
        var acronym = Acronym(target);
        if (acronym.Length >= 2 && exeNorm == acronym) return 40;
        if (exeNorm.Length >= 3 && t.Contains(exeNorm, StringComparison.Ordinal)) return 25;
        if (t.Length >= 3 && exeNorm.Contains(t, StringComparison.Ordinal)) return 10;
        return 0;
    }

    /// <summary>Lowercase letters and digits only: "Counter-Strike 2" → "counterstrike2".</summary>
    internal static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>First letter of each word, whole numbers kept: "Counter-Strike 2" → "cs2", "Phantom Blade Zero" → "pbz".</summary>
    internal static string Acronym(string s)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < s.Length)
        {
            if (!char.IsAsciiLetterOrDigit(s[i])) { i++; continue; }
            var start = i;
            while (i < s.Length && char.IsAsciiLetterOrDigit(s[i])) i++;
            var word = s[start..i];
            if (word.All(char.IsAsciiDigit)) sb.Append(word);
            else sb.Append(char.ToLowerInvariant(word[0]));
        }
        return sb.ToString();
    }

    private static IEnumerable<(string Path, int Depth)> EnumerateExes(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;
        while (queue.Count > 0 && visited++ < MaxDirectories)
        {
            var (dir, depth) = queue.Dequeue();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir, "*.exe", options);
                dirs = depth < MaxDepth ? Directory.GetDirectories(dir, "*", options) : [];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

            foreach (var f in files)
                if (f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !IsNonGameExe(f)) yield return (f, depth);
            foreach (var d in dirs)
                if (!SkippedDirs.Contains(Path.GetFileName(d))) queue.Enqueue((d, depth + 1));
        }
    }
}
