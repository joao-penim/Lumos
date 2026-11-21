using System.Xml;
using System.Xml.Linq;

namespace Lumos.Core.Games;

/// <summary>Reads the <c>__Installer\installerdata.xml</c> manifest that EA app games keep in their install folder.</summary>
public static class EaInstallerData
{
    public static readonly string RelativePath = Path.Combine("__Installer", "installerdata.xml");

    /// <summary>
    /// The game described by an installerdata.xml. Its <see cref="InstalledGame.ExePath"/> is the full-game launcher
    /// the manifest names. For anti-cheat games that is a bootstrapper, so the exe to watch still needs resolving.
    /// </summary>
    public static InstalledGame? Parse(string xml, string installDir)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (XmlException) { return null; }

        var titles = doc.Descendants().Where(e => e.Name.LocalName == "gameTitle").ToList();
        var name = (titles.FirstOrDefault(t => (string?)t.Attribute("locale") == "en_US") ?? titles.FirstOrDefault())?.Value.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;

        var launchers = doc.Descendants().Where(e => e.Name.LocalName == "launcher").ToList();
        var launcher = launchers.FirstOrDefault(l => Child(l, "trial") != "1" && string.IsNullOrEmpty(Child(l, "parameters")))
                       ?? launchers.FirstOrDefault(l => Child(l, "trial") != "1")
                       ?? launchers.FirstOrDefault();
        var exe = ResolveFilePath(Child(launcher, "filePath"), installDir);
        return new InstalledGame(name, GameSources.Ea, installDir, exe);
    }

    /// <summary>
    /// Turns "[HKEY_LOCAL_MACHINE\SOFTWARE\...\Install Dir]Game.exe" into a path under <paramref name="installDir"/>.
    /// The bracketed registry token stands for the install folder.
    /// </summary>
    internal static string? ResolveFilePath(string? filePath, string installDir)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        var path = filePath.Trim();
        if (path.StartsWith('['))
        {
            var close = path.IndexOf(']');
            if (close < 0) return null;
            path = path[(close + 1)..];
        }
        path = path.TrimStart('\\', '/');
        if (path.Length == 0 || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        return Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(Path.Combine(installDir, path));
    }

    /// <summary>Games in the given candidate folders that contain an installerdata.xml.</summary>
    public static List<InstalledGame> FindGames(IEnumerable<string> candidateDirs)
    {
        var games = new List<InstalledGame>();
        foreach (var dir in candidateDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var manifest = Path.Combine(dir, RelativePath);
            try
            {
                if (File.Exists(manifest) && Parse(File.ReadAllText(manifest), Path.TrimEndingDirectorySeparator(dir)) is { } game)
                    games.Add(game);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return games;
    }

    private static string? Child(XElement? e, string name) =>
        e?.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value.Trim();
}
