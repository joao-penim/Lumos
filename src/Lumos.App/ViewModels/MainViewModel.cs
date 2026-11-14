using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumos.App.Services;
using Lumos.Core;
using Lumos.Platform.Windows;

namespace Lumos.App.ViewModels;

/// <summary>Everything the window needs from the view layer (pickers and dialogs).</summary>
public interface IDialogs
{
    Task<string?> PickExeAsync();
    Task<RunningApp?> PickRunningAppAsync(IReadOnlyList<RunningApp> apps);
    Task<bool> EditAppAsync(AppEditorViewModel editor, bool isNew);
    Task<bool> ConfirmAsync(string title, string message, string confirmText);
    Task ShowMessageAsync(string title, string message);
}

public partial class MainViewModel : ObservableObject
{
    private readonly IDialogs _dialogs;
    private readonly DispatcherTimer _statusTimer;
    private LumosSettings _settings = new();
    private List<DisplayStatus> _lastDisplays = [];

    public MainViewModel(IDialogs dialogs)
    {
        _dialogs = dialogs;
        Agent = new AgentClient();
        Settings = new SettingsViewModel(this);
        // Status refresh only runs while this window is open; the process exits when it closes.
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await RefreshStatusAsync());
    }

    public AgentClient Agent { get; }
    public SettingsViewModel Settings { get; }
    public LumosSettings CurrentSettings => _settings;
    public ObservableCollection<AppItemViewModel> Apps { get; } = [];
    public ObservableCollection<DisplayItemViewModel> Displays { get; } = [];
    public IReadOnlyList<DisplayStatus> KnownDisplays => _lastDisplays;

    [ObservableProperty] private bool _agentRunning = true;
    [ObservableProperty] private bool _hasApps;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hdrActive;

    public async Task InitializeAsync()
    {
        _settings = SettingsStore.Load();
        foreach (var app in _settings.Apps) AddItem(app);
        HasApps = Apps.Count > 0;

        AgentRunning = await Agent.EnsureRunningAsync();
        await RefreshStatusAsync();
        Settings.Load();
        _statusTimer.Start();
    }

    private AppItemViewModel AddItem(WatchedApp app)
    {
        var item = new AppItemViewModel(app, this);
        Apps.Add(item);
        _ = LoadIconAsync(item);
        return item;
    }

    private static async Task LoadIconAsync(AppItemViewModel item) => item.Icon = await IconLoader.LoadAsync(item.ExePath);

    public async Task RefreshStatusAsync()
    {
        var status = await Agent.GetStatusAsync();
        AgentRunning = status is not null;
        if (status is null)
        {
            StatusText = "The Lumos agent isn't running, so apps aren't being watched.";
            HdrActive = false;
            return;
        }

        _lastDisplays = status.Displays;
        // Update in place so bindings (and toggle animations) don't reset.
        for (var i = Displays.Count - 1; i >= 0; i--)
            if (!status.Displays.Any(d => d.Id == Displays[i].Id)) Displays.RemoveAt(i);
        foreach (var d in status.Displays)
        {
            var item = Displays.FirstOrDefault(x => x.Id == d.Id);
            if (item is null) Displays.Add(item = new DisplayItemViewModel(this));
            item.Update(d);
        }

        var running = status.Sessions.Where(s => !s.PendingLaunch).Select(s => s.AppId).ToHashSet();
        foreach (var app in Apps) app.IsRunning = running.Contains(app.Model.Id);

        Settings.SyncPaused(status.Paused);
        var names = status.Sessions.Select(s => s.AppName).Distinct().ToArray();
        HdrActive = !status.Paused && status.Displays.Any(d => d.OwnedByLumos && d.HdrEnabled);
        StatusText = status.Paused ? "Lumos is paused. HDR won't be switched automatically."
            : HdrActive ? $"HDR is on for {string.Join(", ", names)}."
            : names.Length > 0 ? $"Running: {string.Join(", ", names)}."
            : Apps.Count == 0 ? "Add the games and apps that should run in HDR."
            : "Waiting for one of your apps to start.";
    }

    public string DescribeTarget(WatchedApp app) => app.Target switch
    {
        DisplayTargetKind.Primary => "Primary display",
        DisplayTargetKind.AllHdrCapable => "All HDR displays",
        DisplayTargetKind.Specific => _lastDisplays.FirstOrDefault(d => string.Equals(d.Id, app.TargetDisplayId, StringComparison.OrdinalIgnoreCase))?.FriendlyName ?? "Specific display (not connected)",
        _ => $"Default ({DescribeDefault()})",
    };

    public string DescribeDefault() => _settings.DefaultTarget switch
    {
        DisplayTargetKind.AllHdrCapable => "all HDR displays",
        DisplayTargetKind.Specific => _lastDisplays.FirstOrDefault(d => string.Equals(d.Id, _settings.DefaultDisplayId, StringComparison.OrdinalIgnoreCase))?.FriendlyName ?? "primary display",
        _ => "primary display",
    };

    public List<TargetOption> BuildTargetOptions(bool includeDefault)
    {
        var list = new List<TargetOption>();
        if (includeDefault) list.Add(new TargetOption($"Default ({DescribeDefault()})", DisplayTargetKind.Default));
        list.Add(new TargetOption("Primary display", DisplayTargetKind.Primary));
        list.Add(new TargetOption("All HDR displays", DisplayTargetKind.AllHdrCapable));
        foreach (var d in _lastDisplays.Where(d => d.HdrSupported))
            list.Add(new TargetOption(d.IsPrimary ? $"{d.FriendlyName} (primary)" : d.FriendlyName, DisplayTargetKind.Specific, d.Id));
        return list;
    }

    // ---- persistence ----

    public async Task SaveAsync()
    {
        _settings.Apps = [.. Apps.Select(a => a.Model)];
        _settings.Paused = SettingsStore.Load().Paused; // the agent owns this flag (tray menu)
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't save settings", e.Message);
            return;
        }
        await Agent.SendAsync(IpcCommands.Reload);
        foreach (var a in Apps) a.NotifyModelChanged();
        HasApps = Apps.Count > 0;
        await RefreshStatusAsync();
    }

    // ---- app commands ----

    [RelayCommand]
    private async Task AddFromFile()
    {
        var path = await _dialogs.PickExeAsync();
        if (path is not null) await AddAppAsync(path);
    }

    [RelayCommand]
    private async Task AddFromRunning()
    {
        var apps = await Task.Run(WindowsShell.GetRunningWindowedApps);
        var picked = await _dialogs.PickRunningAppAsync(apps);
        if (picked is not null) await AddAppAsync(picked.ExePath);
    }

    private async Task AddAppAsync(string exePath)
    {
        var existing = Apps.FirstOrDefault(a => string.Equals(a.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            await EditAsync(existing);
            return;
        }

        var app = new WatchedApp { ExePath = exePath, DisplayName = WindowsShell.GetFriendlyName(exePath) };
        var editor = new AppEditorViewModel(app, BuildTargetOptions(includeDefault: true), _dialogs.PickExeAsync);
        if (!await _dialogs.EditAppAsync(editor, isNew: true)) return;
        editor.ApplyTo(app);
        AddItem(app);
        await SaveAsync();
    }

    public async Task EditAsync(AppItemViewModel item)
    {
        var oldPath = item.ExePath;
        var editor = new AppEditorViewModel(item.Model, BuildTargetOptions(includeDefault: true), _dialogs.PickExeAsync);
        if (!await _dialogs.EditAppAsync(editor, isNew: false)) return;
        editor.ApplyTo(item.Model);
        if (!string.Equals(oldPath, item.ExePath, StringComparison.OrdinalIgnoreCase)) _ = LoadIconAsync(item);
        await SaveAsync();
    }

    public async Task RemoveAsync(AppItemViewModel item)
    {
        if (!await _dialogs.ConfirmAsync($"Remove {item.Name}?",
                "Lumos will stop turning HDR on for this app. Desktop shortcuts created for it will stop working.", "Remove"))
            return;
        Apps.Remove(item);
        await SaveAsync();
    }

    public async Task LaunchAsync(AppItemViewModel item)
    {
        if (!AgentRunning && !(AgentRunning = await Agent.EnsureRunningAsync()))
        {
            await _dialogs.ShowMessageAsync("Agent not running", "Start the Lumos agent first.");
            return;
        }
        var response = await Agent.SendAsync(IpcCommands.Launch, appId: item.Model.Id);
        if (response is { Ok: false }) await _dialogs.ShowMessageAsync("Couldn't launch", response.Error ?? "Unknown error.");
        await RefreshStatusAsync();
    }

    public async Task CreateShortcutAsync(AppItemViewModel item)
    {
        try
        {
            var path = new WindowsShortcutService(AgentClient.AgentPath).CreateDesktopShortcut(item.Model);
            await _dialogs.ShowMessageAsync("Shortcut created",
                $"\"{Path.GetFileNameWithoutExtension(path)}\" is on your desktop. It turns HDR on first, then starts {item.Name}. " +
                "Use it for games that only check HDR when they start.");
        }
        catch (Exception e)
        {
            await _dialogs.ShowMessageAsync("Couldn't create shortcut", e.Message);
        }
    }

    // ---- display / agent commands ----

    public async Task SetHdrAsync(DisplayItemViewModel display, bool enabled)
    {
        var response = await Agent.SendAsync(IpcCommands.SetHdr, displayId: display.Id, enabled: enabled);
        if (response is null || !response.Ok)
            await _dialogs.ShowMessageAsync("Couldn't change HDR", response?.Error ?? "The Lumos agent isn't running.");
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task StartAgent()
    {
        AgentRunning = await Agent.EnsureRunningAsync();
        if (!AgentRunning)
            await _dialogs.ShowMessageAsync("Couldn't start the agent", $"LumosAgent.exe was not found or failed to start.\n{AgentClient.AgentPath}");
        await RefreshStatusAsync();
    }

    public async Task StopAgentAsync()
    {
        if (!await _dialogs.ConfirmAsync("Stop the Lumos agent?",
                "Apps won't switch HDR on until the agent runs again (it starts with Windows if enabled, or when you open Lumos).", "Stop agent"))
            return;
        await Agent.SendAsync(IpcCommands.Exit);
        await Task.Delay(300);
        await RefreshStatusAsync();
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
}
