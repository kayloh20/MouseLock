using System.Runtime.InteropServices;
using static MouseLock.NativeMethods;

namespace MouseLock;

internal class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly CursorLocker _cursorLocker;
    private readonly MessageWindow _messageWindow;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _enabledItem;
    private IntPtr _deviceNotificationHandle;
    private System.Windows.Forms.Timer? _debounceTimer;
    private AppSettings _settings;

    public TrayApplicationContext(AppSettings settings)
    {
        _settings = settings;
        _cursorLocker = new CursorLocker();

        // Status menu item (disabled label)
        _statusItem = new ToolStripMenuItem("Status: Initializing...") { Enabled = false };

        // Enabled toggle
        _enabledItem = new ToolStripMenuItem("Enabled")
        {
            Checked = _settings.LockingEnabled,
            CheckOnClick = true
        };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            _settings.LockingEnabled = _enabledItem.Checked;
            _settings.Save();
            EvaluateAndApplyLocking();
        };

        // Build context menu
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add("Configure...", null, (_, _) => ShowConfigDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        // Create tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = CreateLockIcon(),
            Text = "MouseLock",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowConfigDialog();

        // Create hidden message window for WM_DISPLAYCHANGE / WM_DEVICECHANGE
        _messageWindow = new MessageWindow(this);

        // Register for monitor device notifications
        RegisterForDeviceNotifications();

        // Apply initial locking state
        EvaluateAndApplyLocking();
    }

    private void RegisterForDeviceNotifications()
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = GUID_DEVINTERFACE_MONITOR
        };
        filter.dbcc_size = Marshal.SizeOf(filter);

        IntPtr buffer = Marshal.AllocHGlobal(filter.dbcc_size);
        try
        {
            Marshal.StructureToPtr(filter, buffer, false);
            _deviceNotificationHandle = RegisterDeviceNotification(
                _messageWindow.Handle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal void OnDisplayChanged()
    {
        // Debounce: wait 500ms for the system to settle
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            EvaluateAndApplyLocking();
        };
        _debounceTimer.Start();
    }

    internal void OnPowerResumed()
    {
        // Longer delay for sleep/wake since displays take time to reinit
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            EvaluateAndApplyLocking();
        };
        _debounceTimer.Start();
    }

    private void EvaluateAndApplyLocking()
    {
        var monitors = DisplayManager.GetAllMonitors();

        if (!_settings.LockingEnabled)
        {
            _cursorLocker.StopLocking();
            UpdateStatus("Disabled", false);
            return;
        }

        if (monitors.Count <= 1)
        {
            // Single display - don't lock (user needs cursor wherever it is)
            _cursorLocker.StopLocking();
            UpdateStatus("Inactive (single display)", false);
            return;
        }

        // Multiple monitors - find the one to lock to
        MonitorInfo? targetMonitor = null;

        // Try to find by saved device ID
        if (!string.IsNullOrEmpty(_settings.MonitorDeviceId))
        {
            targetMonitor = monitors.FirstOrDefault(m =>
                !string.IsNullOrEmpty(m.StableDeviceId) &&
                m.StableDeviceId.Equals(_settings.MonitorDeviceId, StringComparison.OrdinalIgnoreCase));
        }

        // Fallback to primary
        targetMonitor ??= monitors.FirstOrDefault(m => m.IsPrimary);

        if (targetMonitor != null)
        {
            if (_cursorLocker.IsLocking)
            {
                _cursorLocker.UpdateLockRect(targetMonitor.MonitorRect);
            }
            else
            {
                _cursorLocker.StartLocking(targetMonitor.MonitorRect);
            }
            UpdateStatus($"Locked to {targetMonitor.FriendlyName}", true);
        }
        else
        {
            _cursorLocker.StopLocking();
            UpdateStatus("Target monitor not found", false);
        }
    }

    private void UpdateStatus(string status, bool locked)
    {
        _statusItem.Text = $"Status: {status}";
        _trayIcon.Text = $"MouseLock - {status}";
        // Truncate tray tooltip to 63 chars (Windows limit)
        if (_trayIcon.Text.Length > 63)
            _trayIcon.Text = _trayIcon.Text[..63];
    }

    private void ShowConfigDialog()
    {
        using var form = new ConfigForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _settings = AppSettings.Load(); // reload saved settings
            _enabledItem.Checked = _settings.LockingEnabled;
            EvaluateAndApplyLocking();
        }
    }

    private void ExitApplication()
    {
        _cursorLocker.StopLocking();
        _cursorLocker.Dispose();

        if (_deviceNotificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_deviceNotificationHandle);
            _deviceNotificationHandle = IntPtr.Zero;
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _messageWindow.DestroyHandle();

        ExitThread();
    }

    private static Icon CreateLockIcon()
    {
        // Generate a simple lock icon programmatically
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        // Draw a simple padlock shape
        using var pen = new Pen(Color.White, 1.5f);
        using var brush = new SolidBrush(Color.FromArgb(60, 120, 216)); // Blue

        // Lock body
        g.FillRectangle(brush, 3, 7, 10, 7);
        g.DrawRectangle(pen, 3, 7, 10, 7);

        // Lock shackle
        g.DrawArc(pen, 5, 2, 6, 8, 180, 180);

        // Keyhole
        using var keyBrush = new SolidBrush(Color.White);
        g.FillEllipse(keyBrush, 7, 9, 3, 3);

        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounceTimer?.Dispose();
            _cursorLocker.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    // Hidden window to receive WM_DISPLAYCHANGE, WM_DEVICECHANGE, WM_POWERBROADCAST
    private class MessageWindow : NativeWindow
    {
        private readonly TrayApplicationContext _owner;

        public MessageWindow(TrayApplicationContext owner)
        {
            _owner = owner;
            var cp = new CreateParams
            {
                Caption = "MouseLock_MessageWindow",
                Style = 0 // invisible
            };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_DISPLAYCHANGE:
                    _owner.OnDisplayChanged();
                    break;

                case WM_DEVICECHANGE:
                    int eventType = m.WParam.ToInt32();
                    if (eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE)
                    {
                        _owner.OnDisplayChanged();
                    }
                    break;

                case WM_POWERBROADCAST:
                    int powerEvent = m.WParam.ToInt32();
                    if (powerEvent == PBT_APMRESUMEAUTOMATIC || powerEvent == PBT_APMRESUMESUSPEND)
                    {
                        _owner.OnPowerResumed();
                    }
                    break;
            }
            base.WndProc(ref m);
        }
    }
}
