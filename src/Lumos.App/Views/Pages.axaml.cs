using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumos.App.Services;
using Lumos.Platform.Windows;

namespace Lumos.App.Views;

public partial class AppsPage : UserControl
{
    public AppsPage() => InitializeComponent();
}

public partial class DisplaysPage : UserControl
{
    public DisplaysPage() => InitializeComponent();
}

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();
}

public partial class AppEditorView : UserControl
{
    public AppEditorView() => InitializeComponent();
}

public partial class RunningAppRow(RunningApp app) : ObservableObject
{
    public RunningApp App { get; } = app;
    public string Name => string.IsNullOrWhiteSpace(App.WindowTitle) ? App.Name : $"{App.Name}  —  {App.WindowTitle}";
    [ObservableProperty] private Bitmap? _icon;
}

public partial class RunningAppsView : UserControl
{
    public event Action? SelectionChanged;
    public event Action? Confirmed;

    public RunningAppsView() : this([]) { }

    public RunningAppsView(IReadOnlyList<RunningApp> apps)
    {
        InitializeComponent();
        var rows = apps.Select(a => new RunningAppRow(a)).ToList();
        List.ItemsSource = rows;
        List.SelectionChanged += (_, _) => SelectionChanged?.Invoke();
        List.DoubleTapped += (_, _) => { if (Selected is not null) Confirmed?.Invoke(); };
        foreach (var row in rows) _ = LoadIconAsync(row);
    }

    public RunningApp? Selected => (List.SelectedItem as RunningAppRow)?.App;

    private static async Task LoadIconAsync(RunningAppRow row) => row.Icon = await IconLoader.LoadAsync(row.App.ExePath);
}
