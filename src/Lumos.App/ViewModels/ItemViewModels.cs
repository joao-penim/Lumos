using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumos.Core;

namespace Lumos.App.ViewModels;

public partial class AppItemViewModel(WatchedApp model, MainViewModel owner) : ObservableObject
{
    public WatchedApp Model { get; } = model;

    public string Name => Model.DisplayName;
    public string ExePath => Model.ExePath;
    public string TargetText => owner.DescribeTarget(Model);
    public string Details => $"{TargetText}  ·  {ExePath}";

    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private bool _isRunning;

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value) return;
            Model.Enabled = value;
            OnPropertyChanged();
            _ = owner.SaveAsync();
        }
    }

    public void NotifyModelChanged() => OnPropertyChanged(string.Empty);

    [RelayCommand] private Task Launch() => owner.LaunchAsync(this);
    [RelayCommand] private Task CreateShortcut() => owner.CreateShortcutAsync(this);
    [RelayCommand] private Task Edit() => owner.EditAsync(this);
    [RelayCommand] private Task Remove() => owner.RemoveAsync(this);
}

public partial class DisplayItemViewModel(MainViewModel owner) : ObservableObject
{
    private bool _syncing;

    public string Id { get; private set; } = "";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isPrimary;
    [ObservableProperty] private bool _hdrSupported;
    [ObservableProperty] private bool _ownedByLumos;
    [ObservableProperty] private bool _hdrEnabled;

    public string Details =>
        (IsPrimary ? "Primary display  ·  " : "") + (HdrSupported ? "HDR capable" : "HDR not supported");

    public string StateText => HdrEnabled && OwnedByLumos ? "Turned on by Lumos" : "";

    public void Update(DisplayStatus status)
    {
        _syncing = true;
        Id = status.Id;
        Name = status.FriendlyName;
        IsPrimary = status.IsPrimary;
        HdrSupported = status.HdrSupported;
        OwnedByLumos = status.OwnedByLumos;
        HdrEnabled = status.HdrEnabled;
        _syncing = false;
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(StateText));
    }

    partial void OnHdrEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        if (!_syncing) _ = owner.SetHdrAsync(this, value);
    }
}

/// <summary>One choice in a "which display" picker.</summary>
public sealed record TargetOption(string Label, DisplayTargetKind Kind, string? DisplayId = null)
{
    public override string ToString() => Label;
}
