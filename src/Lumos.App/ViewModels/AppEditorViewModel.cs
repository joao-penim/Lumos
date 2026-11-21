using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumos.Core;

namespace Lumos.App.ViewModels;

/// <summary>Backs the add/edit dialog. Edits a copy; <see cref="ApplyTo"/> writes back on OK.</summary>
public partial class AppEditorViewModel : ObservableObject
{
    private readonly Func<string?, Task<string?>> _pickExe;
    private readonly string? _browseFolder;

    /// <param name="exeCandidates">Exes found in the game's folder, best first. Offered as alternatives when there's more than one.</param>
    /// <param name="browseFolder">Where Browse starts when there's no exe yet (and labels candidates relative to it).</param>
    public AppEditorViewModel(WatchedApp source, IReadOnlyList<TargetOption> targets, Func<string?, Task<string?>> pickExe,
        IReadOnlyList<string>? exeCandidates = null, string? browseFolder = null)
    {
        _pickExe = pickExe;
        _browseFolder = browseFolder;
        Targets = targets;
        _name = source.DisplayName;
        _exePath = source.ExePath;
        _matchByNameOnly = source.MatchByNameOnly;
        _launchCommand = source.LaunchCommand ?? "";
        _launchArgs = source.LaunchArgs ?? "";
        _selectedTarget = targets.FirstOrDefault(t => t.Kind == source.Target &&
                              (t.Kind != DisplayTargetKind.Specific || string.Equals(t.DisplayId, source.TargetDisplayId, StringComparison.OrdinalIgnoreCase)))
                          ?? targets[0];
        ExeCandidates = [.. (exeCandidates ?? []).Select(p => new ExeChoice(
            browseFolder is null ? Path.GetFileName(p) : Path.GetRelativePath(browseFolder, p), p))];
        _selectedExeCandidate = FindCandidate(_exePath);
    }

    public IReadOnlyList<TargetOption> Targets { get; }

    public IReadOnlyList<ExeChoice> ExeCandidates { get; }
    public bool HasExeCandidates => ExeCandidates.Count > 1;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] private string _name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] private string _exePath;
    [ObservableProperty] private bool _matchByNameOnly;
    [ObservableProperty] private string _launchCommand;
    [ObservableProperty] private string _launchArgs;
    [ObservableProperty] private TargetOption _selectedTarget;
    [ObservableProperty] private ExeChoice? _selectedExeCandidate;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && ExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    partial void OnExePathChanged(string value) => SelectedExeCandidate = FindCandidate(value);

    partial void OnSelectedExeCandidateChanged(ExeChoice? value)
    {
        if (value is not null) ExePath = value.Path;
    }

    private ExeChoice? FindCandidate(string path) =>
        ExeCandidates.FirstOrDefault(c => string.Equals(c.Path, path.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private async Task Browse()
    {
        var current = ExePath.Trim().Trim('"');
        var folder = current.Length > 0 && Path.GetDirectoryName(current) is { } dir && Directory.Exists(dir) ? dir : _browseFolder;
        var path = await _pickExe(folder);
        if (path is null) return;
        ExePath = path;
        if (string.IsNullOrWhiteSpace(Name)) Name = Platform.Windows.WindowsShell.GetFriendlyName(path);
    }

    public void ApplyTo(WatchedApp app)
    {
        app.DisplayName = Name.Trim();
        app.ExePath = ExePath.Trim().Trim('"');
        app.MatchByNameOnly = MatchByNameOnly;
        app.LaunchCommand = string.IsNullOrWhiteSpace(LaunchCommand) ? null : LaunchCommand.Trim();
        app.LaunchArgs = string.IsNullOrWhiteSpace(LaunchArgs) ? null : LaunchArgs.Trim();
        app.Target = SelectedTarget.Kind;
        app.TargetDisplayId = SelectedTarget.DisplayId;
    }
}

/// <summary>An exe offered in the editor, labelled by its path inside the game folder.</summary>
public sealed record ExeChoice(string Label, string Path)
{
    public override string ToString() => Label;
}
