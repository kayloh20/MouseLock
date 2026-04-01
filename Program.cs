namespace MouseLock;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        // Single instance check
        const string mutexName = @"Global\{A7B3C2D1-E4F5-6789-ABCD-EF0123456789}_MouseLock";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("MouseLock is already running.", "MouseLock",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Global exception handlers to release ClipCursor on crash
        Application.ThreadException += (_, _) =>
        {
            NativeMethods.ClipCursor(IntPtr.Zero);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            NativeMethods.ClipCursor(IntPtr.Zero);
        };

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Load settings
        var settings = AppSettings.Load();

        // First run - show config dialog
        if (settings.IsFirstRun)
        {
            using var configForm = new ConfigForm(settings);
            if (configForm.ShowDialog() != DialogResult.OK)
            {
                return; // User cancelled first-run config
            }
            settings = AppSettings.Load(); // reload after save
        }

        Application.Run(new TrayApplicationContext(settings));

        // Prevent GC of mutex
        GC.KeepAlive(_mutex);
    }
}
