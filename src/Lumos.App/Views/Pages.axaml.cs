using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumos.App.Services;
using Lumos.App.ViewModels;
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

public partial class InstalledGamesView : UserControl
{
    private readonly CancellationTokenSource _closed = new();
    private List<InstalledGameRow> _rows = [];

    public event Action? SelectionChanged;
    public event Action? Confirmed;

    public InstalledGamesView()
    {
        InitializeComponent();
        Search.TextChanged += (_, _) => ApplyFilter();
        List.SelectionChanged += (_, _) => SelectionChanged?.Invoke();
        List.DoubleTapped += (_, _) => { if (Selected is not null) Confirmed?.Invoke(); };
        // Rows still queued when the dialog closes don't need their exe or icon any more.
        DetachedFromVisualTree += (_, _) => _closed.Cancel();
    }

    public InstalledGameRow? Selected => List.SelectedItem as InstalledGameRow;

    /// <summary>Shows the scan result and starts resolving exes and icons in the background.</summary>
    public void SetGames(IReadOnlyList<InstalledGameRow> rows)
    {
        _rows = [.. rows];
        Loading.IsVisible = false;
        ApplyFilter();
        Search.Focus();
        foreach (var row in _rows) _ = row.ResolveAsync(_closed.Token);
    }

    public void SetError(string message)
    {
        Loading.IsVisible = false;
        Empty.Text = message;
        Empty.IsVisible = true;
    }

    private void ApplyFilter()
    {
        if (Loading.IsVisible) return;
        var q = Search.Text?.Trim() ?? "";
        var shown = q.Length == 0 ? _rows : _rows.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        List.ItemsSource = shown;
        Empty.Text = _rows.Count == 0
            ? "No games found. If your games are in a folder Lumos doesn't know about, add it in Settings → Game folders."
            : "No games match your search.";
        Empty.IsVisible = shown.Count == 0;
    }
}
