using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ModbusLogger.App;

public partial class App : Application
{
    private static readonly string CrashLogPath =
        Path.Combine(AppContext.BaseDirectory, "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                $"Prišlo je do napake:\n\n{args.Exception.Message}\n\nPodrobnosti so v {CrashLogPath}",
                "Napaka", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch
        {
            // če zapis spodleti, ni kaj več narediti
        }
    }
}
