using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lumos.App.ViewModels;
using Lumos.App.Views;

namespace Lumos.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the window ends the process: the UI never lingers in memory. The agent keeps running.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var args = desktop.Args ?? [];
            var pageIndex = Array.IndexOf(args, "--page");
            var window = new MainWindow { InitialPage = pageIndex >= 0 && pageIndex + 1 < args.Length ? args[pageIndex + 1] : "apps" };
            var vm = new MainViewModel(window);
            window.DataContext = vm;
            desktop.MainWindow = window;
            _ = vm.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
