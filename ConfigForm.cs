namespace MouseLock;

internal class ConfigForm : Form
{
    private readonly AppSettings _settings;
    private readonly ListView _monitorList;
    private readonly Button _okButton;
    private readonly Button _cancelButton;
    private readonly Button _identifyButton;
    private readonly Button _refreshButton;
    private readonly Label _instructionLabel;
    private readonly CheckBox _startWithWindowsCheck;

    public ConfigForm(AppSettings settings)
    {
        _settings = settings;

        Text = "MouseLock - Configure";
        Size = new Size(550, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;

        _instructionLabel = new Label
        {
            Text = "Select the monitor to lock your cursor to.\nThe cursor will be confined to this display when multiple monitors are connected.",
            Location = new Point(12, 12),
            Size = new Size(510, 40),
            AutoSize = false
        };

        _monitorList = new ListView
        {
            Location = new Point(12, 58),
            Size = new Size(510, 200),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _monitorList.Columns.Add("Display", 200);
        _monitorList.Columns.Add("Resolution", 100);
        _monitorList.Columns.Add("Position", 100);
        _monitorList.Columns.Add("Primary", 60);

        _identifyButton = new Button
        {
            Text = "Identify",
            Location = new Point(12, 268),
            Size = new Size(80, 28)
        };
        _identifyButton.Click += IdentifyButton_Click;

        _refreshButton = new Button
        {
            Text = "Refresh",
            Location = new Point(98, 268),
            Size = new Size(80, 28)
        };
        _refreshButton.Click += (_, _) => PopulateMonitorList();

        _startWithWindowsCheck = new CheckBox
        {
            Text = "Start with Windows",
            Location = new Point(12, 305),
            Size = new Size(200, 24),
            Checked = _settings.StartWithWindows
        };

        _okButton = new Button
        {
            Text = "OK",
            Location = new Point(366, 330),
            Size = new Size(75, 28),
            DialogResult = DialogResult.OK
        };
        _okButton.Click += OkButton_Click;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(447, 330),
            Size = new Size(75, 28),
            DialogResult = DialogResult.Cancel
        };

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.AddRange([
            _instructionLabel, _monitorList, _identifyButton, _refreshButton,
            _startWithWindowsCheck, _okButton, _cancelButton
        ]);

        PopulateMonitorList();
    }

    private void PopulateMonitorList()
    {
        _monitorList.Items.Clear();
        var monitors = DisplayManager.GetAllMonitors();

        if (monitors.Count == 0)
        {
            _monitorList.Items.Add(new ListViewItem("No displays detected"));
            return;
        }

        foreach (var monitor in monitors)
        {
            int w = monitor.MonitorRect.Right - monitor.MonitorRect.Left;
            int h = monitor.MonitorRect.Bottom - monitor.MonitorRect.Top;

            var item = new ListViewItem(monitor.FriendlyName);
            item.SubItems.Add($"{w} x {h}");
            item.SubItems.Add($"{monitor.MonitorRect.Left}, {monitor.MonitorRect.Top}");
            item.SubItems.Add(monitor.IsPrimary ? "Yes" : "No");
            item.Tag = monitor;

            _monitorList.Items.Add(item);

            // Pre-select the saved monitor or primary
            bool shouldSelect = false;
            if (!string.IsNullOrEmpty(_settings.MonitorDeviceId))
            {
                shouldSelect = monitor.StableDeviceId.Equals(
                    _settings.MonitorDeviceId, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                shouldSelect = monitor.IsPrimary;
            }

            if (shouldSelect)
            {
                item.Selected = true;
                item.Focused = true;
                _monitorList.EnsureVisible(item.Index);
            }
        }
    }

    private void IdentifyButton_Click(object? sender, EventArgs e)
    {
        var monitors = DisplayManager.GetAllMonitors();
        var overlays = new List<Form>();

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var overlay = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                BackColor = Color.Black,
                Opacity = 0.75,
                TopMost = true,
                ShowInTaskbar = false,
                Location = new Point(monitor.MonitorRect.Left, monitor.MonitorRect.Top),
                Size = new Size(
                    monitor.MonitorRect.Right - monitor.MonitorRect.Left,
                    monitor.MonitorRect.Bottom - monitor.MonitorRect.Top)
            };

            var label = new Label
            {
                Text = $"{i + 1}\n{monitor.FriendlyName}",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 72, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            overlay.Controls.Add(label);
            overlays.Add(overlay);
        }

        // Show all overlays
        foreach (var overlay in overlays)
            overlay.Show();

        // Close after 2 seconds
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            foreach (var overlay in overlays)
            {
                overlay.Close();
                overlay.Dispose();
            }
        };
        timer.Start();
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        if (_monitorList.SelectedItems.Count == 0 || _monitorList.SelectedItems[0].Tag is not MonitorInfo selected)
        {
            MessageBox.Show("Please select a monitor.", "MouseLock",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        _settings.MonitorDeviceId = selected.StableDeviceId;
        _settings.MonitorFriendlyName = selected.FriendlyName;
        _settings.StartWithWindows = _startWithWindowsCheck.Checked;

        // Handle startup registration
        UpdateStartupRegistration(_settings.StartWithWindows);

        _settings.Save();
    }

    private static void UpdateStartupRegistration(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                string exePath = Application.ExecutablePath;
                key.SetValue("MouseLock", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("MouseLock", false);
            }
        }
        catch
        {
            // Best effort
        }
    }
}
