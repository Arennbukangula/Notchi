using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WindowsNotch;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
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
