namespace ImageLocker;

/// <summary>
/// Standard WPF application entry point.
/// Registers global exception handlers so startup errors are shown in a message box.
/// </summary>

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Runs when the WPF application starts.
    /// Hooks unhandled exception events before the main window continues loading.
    /// </summary>
    /// <param name="e">Startup arguments passed by WPF.</param>
    
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // catches exceptions thrown on the WPF UI thread during startup/runtime.
        this.DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                args.Exception.ToString(),
                "Startup Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            args.Handled = true;
            this.Shutdown();
        };

        // catches exceptions that escape to the AppDomain level.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                args.ExceptionObject?.ToString() ?? "Unknown startup error",
                "Fatal Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        };

        base.OnStartup(e);
    }
}
