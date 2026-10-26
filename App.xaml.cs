using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WindowsNotch;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "Notchi.SingleInstance";

    private Mutex? _singleInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isFirstInstance = true;

        try
        {
            _singleInstanceMutex = new Mutex(
                true,
                SingleInstanceMutexName,
                out isFirstInstance);
        }
        catch (Exception exception)
        {
            CrashLogger.Log("SingleInstanceMutex", exception);
        }

        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        Localization.Apply(SettingsStore.Load().Language);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLogger.Log("DispatcherUnhandledException", e.Exception);

        if (e.Exception is InvalidOperationException exception &&
            exception.Message.Contains("while a Window is closing",
                StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CrashLogger.Log("AppDomain.UnhandledException", exception);
        }
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.Log("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
