using System;
using System.Threading;
using System.Windows.Forms;
using Personal_TaskBar.Services;

namespace Personal_TaskBar;

/// <summary>
/// Application entry point. Enforces single-instance via a named Mutex.
/// If another instance is already running, brings its window to the foreground.
/// </summary>
internal static class Program
{
    // Unique mutex name that identifies this application instance
    private const string MutexName = "Personal_TaskBar_SingleInstance_Mutex";

    [STAThread]
    static void Main()
    {
        // Try to create a named system-wide mutex. The "owned" flag means we acquire it immediately.
        using var mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance owns the mutex – find its window and bring it forward
            NativeMethods.BroadcastActivateMessage();
            return;
        }

        ApplicationConfiguration.Initialize();

        // Catch exceptions on the UI message-pump thread
        Application.ThreadException += (_, ex) =>
            MessageBox.Show($"Error:\n\n{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                            "Personal TaskBar – Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // Catch exceptions that happen BEFORE the message loop starts (e.g. constructor)
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            var msg = ex.ExceptionObject is Exception e
                ? $"{e.GetType().Name}: {e.Message}\n\n{e.StackTrace}"
                : ex.ExceptionObject?.ToString() ?? "Unknown error";
            MessageBox.Show($"Fatal startup error:\n\n{msg}",
                            "Personal TaskBar – Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        // Enable visual styles so controls render using the current Windows theme
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Initialize config service (creates default files if missing)
        var configService = new ConfigService();
        configService.EnsureDefaults();

        Application.Run(new MainForm(configService));
    }
}
