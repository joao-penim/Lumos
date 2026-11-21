namespace Lumos.Core;

/// <summary>Which display(s) an app's HDR session applies to.</summary>
public enum DisplayTargetKind
{
    /// <summary>Use the global default from <see cref="LumosSettings.DefaultTarget"/>.</summary>
    Default,
    Primary,
    AllHdrCapable,
    Specific,
}

/// <summary>An application that should run with HDR enabled.</summary>
public sealed class WatchedApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";

    /// <summary>Full path to the executable, e.g. C:\Games\Foo\Foo.exe.</summary>
    public string ExePath { get; set; } = "";

    /// <summary>Match any process with the same file name, regardless of folder.</summary>
    public bool MatchByNameOnly { get; set; }

    /// <summary>What "Launch with Lumos" starts. Defaults to <see cref="ExePath"/>; may be a URI such as steam://rungameid/123.</summary>
    public string? LaunchCommand { get; set; }
    public string? LaunchArgs { get; set; }

    public DisplayTargetKind Target { get; set; } = DisplayTargetKind.Default;

    /// <summary>Display id when <see cref="Target"/> is <see cref="DisplayTargetKind.Specific"/>.</summary>
    public string? TargetDisplayId { get; set; }

    public bool Enabled { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public string ExeFileName => Path.GetFileName(ExePath);
}

public sealed class LumosSettings
{
    public List<WatchedApp> Apps { get; set; } = [];
    public DisplayTargetKind DefaultTarget { get; set; } = DisplayTargetKind.Primary;
    public string? DefaultDisplayId { get; set; }

    /// <summary>Seconds to wait after the last watched app exits before turning HDR off (covers launcher restarts).</summary>
    public int GraceDelaySeconds { get; set; } = 3;

    public bool Paused { get; set; }

    /// <summary>Extra folders to look in for installed games, on top of the launchers found automatically. Each subfolder is one game.</summary>
    public List<string> GameFolders { get; set; } = [];
}

/// <summary>A connected display as reported by the platform.</summary>
/// <param name="Id">Stable identifier (Windows: monitor device path).</param>
public sealed record DisplayInfo(string Id, string FriendlyName, bool IsPrimary, bool HdrSupported, bool HdrEnabled);
