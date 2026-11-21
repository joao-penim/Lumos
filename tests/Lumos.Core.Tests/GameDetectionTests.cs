using Lumos.Core.Games;
using Xunit;

namespace Lumos.Core.Tests;

public sealed class GameDetectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lumos-games-{Guid.NewGuid():N}");

    public GameDetectionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Touch(string relative, int megabytes = 0)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var f = File.Create(path);
        f.SetLength(megabytes * 1024L * 1024L);
        return path;
    }

    // ---- Steam ----

    private const string LibraryFolders = """
        "libraryfolders"
        {
            "0"
            {
                "path"      "C:\\Program Files (x86)\\Steam"
                "label"     ""
                "apps"
                {
                    "730"       "71797149735"
                }
            }
            // a second library on another drive
            "1"
            {
                "path"      "D:\\SteamLibrary"
            }
        }
        """;

    [Fact]
    public void Steam_library_folders_are_read_from_vdf()
    {
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamLibrary.ParseLibraryFolders(LibraryFolders));
    }

    [Fact]
    public void Steam_app_manifest_gives_id_name_and_folder()
    {
        var app = SteamLibrary.ParseAppManifest("""
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
                "StateFlags"        "4"
                "installdir"        "Counter-Strike Global Offensive"
            }
            """);
        Assert.Equal(new SteamLibrary.SteamApp("730", "Counter-Strike 2", "Counter-Strike Global Offensive"), app);
    }

    [Theory]
    [InlineData("228980", "Steamworks Common Redistributables", "4")]
    [InlineData("1070560", "Steam Linux Runtime", "4")]
    [InlineData("1234", "Some Game", "1026")] // still downloading
    public void Steam_tools_and_unfinished_installs_are_skipped(string id, string name, string flags)
    {
        var acf = $$"""
            "AppState" { "appid" "{{id}}" "name" "{{name}}" "StateFlags" "{{flags}}" "installdir" "X" }
            """;
        Assert.Null(SteamLibrary.ParseAppManifest(acf));
    }

    [Fact]
    public void Steam_games_are_found_across_libraries()
    {
        var steam = Path.Combine(_root, "Steam");
        var other = Path.Combine(_root, "Library2");
        Touch(@"Steam\steamapps\common\Peak\PEAK.exe");
        Touch(@"Library2\steamapps\common\Deadlock\game\bin\win64\deadlock.exe");
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders" { "0" { "path" "{{steam.Replace(@"\", @"\\")}}" } "1" { "path" "{{other.Replace(@"\", @"\\")}}" } }
            """);
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_1.acf"),
            """ "AppState" { "appid" "1" "name" "PEAK" "StateFlags" "4" "installdir" "Peak" } """);
        File.WriteAllText(Path.Combine(other, "steamapps", "appmanifest_2.acf"),
            """ "AppState" { "appid" "2" "name" "Deadlock" "StateFlags" "4" "installdir" "Deadlock" } """);
        File.WriteAllText(Path.Combine(other, "steamapps", "appmanifest_3.acf"),
            """ "AppState" { "appid" "3" "name" "Uninstalled" "StateFlags" "4" "installdir" "Gone" } """);

        var games = SteamLibrary.FindGames(steam).OrderBy(g => g.Name).ToList();

        Assert.Equal(["Deadlock", "PEAK"], games.Select(g => g.Name));
        Assert.All(games, g => Assert.Equal(GameSources.Steam, g.Source));
        Assert.Equal("steam://rungameid/2", games[0].LaunchCommand);
    }

    // ---- Epic ----

    [Fact]
    public void Epic_manifest_gives_exe_and_launch_uri()
    {
        var game = EpicManifest.Parse("""
            {
              "DisplayName": "Fortnite",
              "InstallLocation": "C:\\Epic\\Fortnite",
              "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteLauncher.exe",
              "AppName": "Fortnite",
              "MainGameAppName": "Fortnite",
              "AppCategories": ["public", "games", "applications"],
              "bIsIncompleteInstall": false
            }
            """);
        Assert.NotNull(game);
        Assert.Equal("Fortnite", game.Name);
        Assert.Equal(Path.GetFullPath(@"C:\Epic\Fortnite\FortniteGame\Binaries\Win64\FortniteLauncher.exe"), game.ExePath);
        Assert.Equal("com.epicgames.launcher://apps/Fortnite?action=launch&silent=true", game.LaunchCommand);
    }

    [Theory]
    [InlineData("""{ "DisplayName": "DLC", "InstallLocation": "C:\\G", "AppName": "dlc1", "MainGameAppName": "game", "AppCategories": ["games"] }""")]
    [InlineData("""{ "DisplayName": "Engine", "InstallLocation": "C:\\G", "AppName": "UE_5", "AppCategories": ["engines"] }""")]
    [InlineData("""{ "DisplayName": "Half", "InstallLocation": "C:\\G", "AppName": "x", "AppCategories": ["games"], "bIsIncompleteInstall": true }""")]
    public void Epic_dlc_non_games_and_partial_installs_are_skipped(string json) => Assert.Null(EpicManifest.Parse(json));

    // ---- EA app ----

    private const string EaManifest = """
        <?xml version='1.0' encoding='utf-8'?>
        <DiPManifest version="4.0">
          <gameTitles>
            <gameTitle locale="pt_PT">Jogo</gameTitle>
            <gameTitle locale="en_US">EA SPORTS FC 27</gameTitle>
          </gameTitles>
          <runtime>
            <launcher uid="1-3">
              <filePath>[HKEY_LOCAL_MACHINE\SOFTWARE\EA Sports\EA SPORTS FC 27\Install Dir]FC27_Trial.exe</filePath>
              <parameters>-trial</parameters>
              <trial>1</trial>
            </launcher>
            <launcher uid="2-3">
              <filePath>[HKEY_LOCAL_MACHINE\SOFTWARE\EA Sports\EA SPORTS FC 27\Install Dir]EAAntiCheat.GameServiceLauncher.exe</filePath>
              <parameters />
              <trial>0</trial>
            </launcher>
          </runtime>
        </DiPManifest>
        """;

    [Fact]
    public void Ea_manifest_gives_english_title_and_full_game_launcher()
    {
        var game = EaInstallerData.Parse(EaManifest, @"C:\EA Games\FC 27");
        Assert.NotNull(game);
        Assert.Equal("EA SPORTS FC 27", game.Name);
        Assert.Equal(Path.GetFullPath(@"C:\EA Games\FC 27\EAAntiCheat.GameServiceLauncher.exe"), game.ExePath);
    }

    [Fact]
    public void Ea_anti_cheat_launcher_is_not_watched_but_the_game_exe_is()
    {
        var dir = Path.Combine(_root, "FC 27");
        Touch(@"FC 27\EAAntiCheat.GameServiceLauncher.exe", 20);
        Touch(@"FC 27\FC27.exe", 500);
        Touch(@"FC 27\FC27_Trial.exe", 520);
        Touch(@"FC 27\FC27_Showcase.exe", 220);
        Touch(@"FC 27\__Installer\Touchup.exe", 1);
        File.WriteAllText(Path.Combine(dir, EaInstallerData.RelativePath), EaManifest);

        var game = Assert.Single(EaInstallerData.FindGames([dir]));
        var candidates = GameExeResolver.Resolve(game);

        Assert.Equal(Path.Combine(dir, "FC27.exe"), candidates[0]);
        Assert.DoesNotContain(candidates, c => c.Contains("AntiCheat", StringComparison.OrdinalIgnoreCase) || c.Contains("Touchup"));
    }

    // ---- exe resolver ----

    [Fact]
    public void Unreal_shipping_exe_beats_the_bootstrapper()
    {
        Touch(@"SMITE 2\Windows\Hemingway.exe");
        Touch(@"SMITE 2\Windows\start_protected_game.exe", 4);
        Touch(@"SMITE 2\Windows\Engine\Binaries\Win64\CrashReportClient.exe", 25);
        var shipping = Touch(@"SMITE 2\Windows\Hemingway\Binaries\Win64\Hemingway-Win64-Shipping.exe", 190);

        var ranked = GameExeResolver.Rank(Path.Combine(_root, "SMITE 2"), "SMITE 2");

        Assert.Equal(shipping, ranked[0]);
        Assert.DoesNotContain(ranked, p => p.Contains("start_protected_game") || p.Contains("CrashReport"));
    }

    [Fact]
    public void Real_exe_under_binaries_beats_small_root_stub_with_the_same_name()
    {
        Touch(@"Hogwarts Legacy\HogwartsLegacy.exe");
        var real = Touch(@"Hogwarts Legacy\Phoenix\Binaries\Win64\HogwartsLegacy.exe", 360);
        Assert.Equal(real, GameExeResolver.Rank(Path.Combine(_root, "Hogwarts Legacy"), "Hogwarts Legacy")[0]);
    }

    [Fact]
    public void Name_match_beats_a_bigger_unrelated_exe()
    {
        var game = Touch(@"Deadlock\game\bin\win64\deadlock.exe", 1);
        Touch(@"Deadlock\game\bin\win64\toolsapp.exe", 40);
        Assert.Equal(game, GameExeResolver.Rank(Path.Combine(_root, "Deadlock"), "Deadlock")[0]);
    }

    [Fact]
    public void Acronym_of_the_game_name_matches()
    {
        var cs2 = Touch(@"Counter-Strike Global Offensive\game\bin\win64\cs2.exe", 3);
        Touch(@"Counter-Strike Global Offensive\game\bin\win64\vconsole2.exe", 5);
        Touch(@"Counter-Strike Global Offensive\game\csgo\bin\legacy\csgo_legacy_app.exe", 2);
        Assert.Equal(cs2, GameExeResolver.Rank(Path.Combine(_root, "Counter-Strike Global Offensive"), "Counter-Strike 2")[0]);
    }

    [Fact]
    public void Redistributables_and_uninstallers_are_ignored()
    {
        Touch(@"Game\_CommonRedist\vcredist\2022\VC_redist.x64.exe", 18);
        Touch(@"Game\unins000.exe", 3);
        Touch(@"Game\UnityCrashHandler64.exe", 2);
        var exe = Touch(@"Game\Game.exe", 1);
        Assert.Equal([exe], GameExeResolver.Rank(Path.Combine(_root, "Game"), "Game"));
    }

    [Theory]
    [InlineData("Counter-Strike 2", "cs2")]
    [InlineData("Phantom Blade Zero", "pbz")]
    [InlineData("PUBG: BATTLEGROUNDS", "pb")]
    public void Acronyms_keep_whole_numbers(string name, string expected) => Assert.Equal(expected, GameExeResolver.Acronym(name));

    // ---- custom folders ----

    [Fact]
    public void Custom_folder_lists_each_subfolder_with_a_game_exe()
    {
        Touch(@"Games\Alpha\Alpha.exe");
        Touch(@"Games\Beta\bin\Beta.exe");
        Touch(@"Games\Empty\readme.txt");
        Touch(@"Games\OnlyRedist\_CommonRedist\vc_redist.x64.exe");

        var games = CustomFolderScanner.Scan(Path.Combine(_root, "Games"));

        Assert.Equal(["Alpha", "Beta"], games.Select(g => g.Name).Order());
        Assert.All(games, g => Assert.Equal(GameSources.Folder, g.Source));
    }

    [Fact]
    public void Custom_folder_that_is_itself_a_game_is_one_game()
    {
        Touch(@"Solo\Solo.exe");
        Touch(@"Solo\Data\stuff.exe");
        var game = Assert.Single(CustomFolderScanner.Scan(Path.Combine(_root, "Solo")));
        Assert.Equal("Solo", game.Name);
    }

    [Fact]
    public void Install_dir_contains_only_paths_inside_it()
    {
        var game = new InstalledGame("G", GameSources.Folder, @"C:\Games\Foo");
        Assert.True(game.Contains(@"C:\Games\Foo\bin\foo.exe"));
        Assert.True(game.Contains(@"c:\games\foo\foo.exe"));
        Assert.False(game.Contains(@"C:\Games\FooBar\foo.exe"));
        Assert.False(game.Contains(@"C:\Games\Foo"));
    }
}
