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

        // Catch any unhandled exception on the UI thread and show it rather than silently crashing
        Application.ThreadException += (_, ex) =>
            MessageBox.Show($"Unhandled error:\n\n{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                            "Personal TaskBar – Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // Enable visual styles so controls render using the current Windows theme
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Initialize config service (creates default files if missing)
        var configService = new ConfigService();
        configService.EnsureDefaults();

        Application.Run(new MainForm(configService));
    }
}
