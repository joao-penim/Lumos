using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumos.App.Services;
using Lumos.Core;
using Lumos.Platform.Windows;

namespace Lumos.App.ViewModels;

public partial class SettingsViewModel(MainViewModel owner) : ObservableObject
{
    private readonly RegistryAutostart _autostart = new(AgentClient.AgentPath);
    private bool _loading;

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _paused;
    [ObservableProperty] private int _graceDelaySeconds = 3;
    [ObservableProperty] private List<TargetOption> _defaultTargets = [];
    [ObservableProperty] private TargetOption? _selectedDefaultTarget;

    public string Version { get; } = typeof(SettingsViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";

    /// <summary>Extra folders searched by "Choose from installed games".</summary>
    public ObservableCollection<GameFolderItem> GameFolders { get; } = [];

    public void Load()
    {
        _loading = true;
        var s = owner.CurrentSettings;
        GameFolders.Clear();
        foreach (var folder in s.GameFolders) GameFolders.Add(new GameFolderItem(folder, this));
        StartWithWindows = _autostart.IsEnabled;
        GraceDelaySeconds = s.GraceDelaySeconds;
        Paused = s.Paused;
        DefaultTargets = owner.BuildTargetOptions(includeDefault: false);
        SelectedDefaultTarget = DefaultTargets.FirstOrDefault(t => t.Kind == s.DefaultTarget &&
                                    (t.Kind != DisplayTargetKind.Specific || string.Equals(t.DisplayId, s.DefaultDisplayId, StringComparison.OrdinalIgnoreCase)))
                                ?? DefaultTargets.FirstOrDefault();
        _loading = false;
    }

    public void SyncPaused(bool paused)
    {
        _loading = true;
        Paused = paused;
        _loading = false;
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        try { _autostart.SetEnabled(value); }
        catch (Exception) { _loading = true; StartWithWindows = _autostart.IsEnabled; _loading = false; }
    }

    partial void OnPausedChanged(bool value)
    {
        if (_loading) return;
        _ = owner.Agent.SendAsync(value ? IpcCommands.Pause : IpcCommands.Resume);
    }

    partial void OnGraceDelaySecondsChanged(int value)
    {
        if (_loading) return;
        owner.CurrentSettings.GraceDelaySeconds = Math.Clamp(value, 0, 60);
        _ = owner.SaveAsync();
    }

    partial void OnSelectedDefaultTargetChanged(TargetOption? value)
    {
        if (_loading || value is null) return;
        owner.CurrentSettings.DefaultTarget = value.Kind;
        owner.CurrentSettings.DefaultDisplayId = value.DisplayId;
        _ = owner.SaveAsync();
    }

    [RelayCommand]
    private async Task AddGameFolder()
    {
        var picked = await owner.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(picked)) return;
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picked));
        var folders = owner.CurrentSettings.GameFolders;
        if (folders.Any(f => string.Equals(Path.TrimEndingDirectorySeparator(f), path, StringComparison.OrdinalIgnoreCase))) return;

        folders.Add(path);
        GameFolders.Add(new GameFolderItem(path, this));
        await owner.SaveAsync();
    }

    internal async Task RemoveGameFolderAsync(GameFolderItem item)
    {
        owner.CurrentSettings.GameFolders.RemoveAll(f => string.Equals(f, item.Path, StringComparison.OrdinalIgnoreCase));
        GameFolders.Remove(item);
        await owner.SaveAsync();
    }

    [RelayCommand] private static void OpenConfigFolder() => MainViewModel.OpenFolder(LumosPaths.ConfigDir);
    [RelayCommand] private Task StopAgent() => owner.StopAgentAsync();
}

/// <summary>One row in Settings → Game folders.</summary>
public partial class GameFolderItem(string path, SettingsViewModel owner) : ObservableObject
{
    public string Path { get; } = path;

    [RelayCommand] private Task Remove() => owner.RemoveGameFolderAsync(this);
    [RelayCommand] private void Open() => MainViewModel.OpenFolder(Path);
}
