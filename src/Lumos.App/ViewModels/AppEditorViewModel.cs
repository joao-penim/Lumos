using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumos.Core;

namespace Lumos.App.ViewModels;

/// <summary>Backs the add/edit dialog. Edits a copy; <see cref="ApplyTo"/> writes back on OK.</summary>
public partial class AppEditorViewModel : ObservableObject
{
    private readonly Func<Task<string?>> _pickExe;

    public AppEditorViewModel(WatchedApp source, IReadOnlyList<TargetOption> targets, Func<Task<string?>> pickExe)
    {
        _pickExe = pickExe;
        Targets = targets;
        _name = source.DisplayName;
        _exePath = source.ExePath;
        _matchByNameOnly = source.MatchByNameOnly;
        _launchCommand = source.LaunchCommand ?? "";
        _launchArgs = source.LaunchArgs ?? "";
        _selectedTarget = targets.FirstOrDefault(t => t.Kind == source.Target &&
                              (t.Kind != DisplayTargetKind.Specific || string.Equals(t.DisplayId, source.TargetDisplayId, StringComparison.OrdinalIgnoreCase)))
                          ?? targets[0];
    }

    public IReadOnlyList<TargetOption> Targets { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] private string _name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] private string _exePath;
    [ObservableProperty] private bool _matchByNameOnly;
    [ObservableProperty] private string _launchCommand;
    [ObservableProperty] private string _launchArgs;
    [ObservableProperty] private TargetOption _selectedTarget;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && ExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task Browse()
    {
        var path = await _pickExe();
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
