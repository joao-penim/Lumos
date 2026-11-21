using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Lumos.App.ViewModels;
using Lumos.Platform.Windows;

namespace Lumos.App.Views;

public partial class MainWindow : FAAppWindow, IDialogs
{
    private AppsPage? _apps;
    private DisplaysPage? _displays;
    private SettingsPage? _settings;

    public MainWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;
        TransparencyLevelHint = [Avalonia.Controls.WindowTransparencyLevel.Mica];

        Nav.SelectionChanged += (_, e) => Navigate(e.IsSettingsSelected ? "settings" : (e.SelectedItem as FANavigationViewItem)?.Tag as string);
        Loaded += (_, _) => SelectPage(InitialPage);
    }

    /// <summary>Page to show first ("apps", "displays" or "settings"), from the --page argument.</summary>
    public string InitialPage { get; set; } = "apps";

    private void SelectPage(string tag)
    {
        Nav.SelectedItem = tag == "settings"
            ? Nav.SettingsItem
            : Nav.MenuItems.OfType<FANavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag) ?? Nav.MenuItems[0];
        Navigate(tag);
    }

    private MainViewModel Vm => (MainViewModel)DataContext!;

    private void Navigate(string? tag)
    {
        PageHost.Content = tag switch
        {
            "displays" => _displays ??= new DisplaysPage { DataContext = Vm },
            "settings" => _settings ??= new SettingsPage { DataContext = Vm.Settings },
            _ => _apps ??= new AppsPage { DataContext = Vm },
        };
        if (tag == "settings") Vm.Settings.Load(); // display list may have changed
    }

    // ---- IDialogs ----

    public async Task<string?> PickExeAsync(string? startFolder = null)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an app or game",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Applications") { Patterns = ["*.exe"] }],
            SuggestedStartLocation = await TryGetFolderAsync(startFolder),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder with games",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return null;
        try { return await StorageProvider.TryGetFolderFromPathAsync(path); }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task<InstalledGameRow?> PickInstalledGameAsync(Task<List<InstalledGameRow>> scan)
    {
        var view = new InstalledGamesView();
        var dialog = new FAContentDialog
        {
            Title = "Choose an installed game",
            Content = view,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        view.SelectionChanged += () => dialog.IsPrimaryButtonEnabled = view.Selected is not null;
        view.Confirmed += () => dialog.Hide(FAContentDialogResult.Primary);
        _ = FillAsync();
        var result = await dialog.ShowAsync(this);
        return result == FAContentDialogResult.Primary ? view.Selected : null;

        async Task FillAsync()
        {
            try { view.SetGames(await scan); }
            catch (Exception e) { view.SetError($"Couldn't look for games: {e.Message}"); }
        }
    }

    public async Task<RunningApp?> PickRunningAppAsync(IReadOnlyList<RunningApp> apps)
    {
        var view = new RunningAppsView(apps);
        var dialog = new FAContentDialog
        {
            Title = "Choose a running app",
            Content = view,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        view.SelectionChanged += () => dialog.IsPrimaryButtonEnabled = view.Selected is not null;
        view.Confirmed += () => dialog.Hide(FAContentDialogResult.Primary);
        var result = await dialog.ShowAsync(this);
        return result == FAContentDialogResult.Primary ? view.Selected : null;
    }

    public async Task<bool> EditAppAsync(AppEditorViewModel editor, bool isNew)
    {
        var dialog = new FAContentDialog
        {
            Title = isNew ? "Add app" : "Edit app",
            Content = new AppEditorView { DataContext = editor },
            PrimaryButtonText = isNew ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = editor.IsValid,
        };
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppEditorViewModel.IsValid)) dialog.IsPrimaryButtonEnabled = editor.IsValid;
        };
        return await dialog.ShowAsync(this) == FAContentDialogResult.Primary;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Close,
        };
        return await dialog.ShowAsync(this) == FAContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
            CloseButtonText = "OK",
            DefaultButton = FAContentDialogButton.Close,
        };
        await dialog.ShowAsync(this);
    }
}
