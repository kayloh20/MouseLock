using static MouseLock.NativeMethods;

namespace MouseLock;

internal class MonitorInfo
{
    public IntPtr Handle { get; init; }
    public RECT MonitorRect { get; init; }
    public RECT WorkArea { get; init; }
    public string DeviceName { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public bool IsPrimary { get; init; }

    public string DisplayLabel =>
        $"{FriendlyName} ({MonitorRect.Right - MonitorRect.Left}x{MonitorRect.Bottom - MonitorRect.Top})"
        + (IsPrimary ? " [Primary]" : "");

    // Extract the stable portion of DeviceID (e.g., "MONITOR\SAM0382")
    // The full ID looks like "MONITOR\SAM0382\{guid}\instance"
    public string StableDeviceId
    {
        get
        {
            if (string.IsNullOrEmpty(DeviceId)) return "";
            var parts = DeviceId.Split('\\');
            return parts.Length >= 2 ? $"{parts[0]}\\{parts[1]}" : DeviceId;
        }
    }
}

internal static class DisplayManager
{
    public static List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var info = new MONITORINFOEX();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMonitor, ref info))
            {
                var (friendlyName, deviceId) = GetDisplayDeviceInfo(info.szDevice);

                monitors.Add(new MonitorInfo
                {
                    Handle = hMonitor,
                    MonitorRect = info.rcMonitor,
                    WorkArea = info.rcWork,
                    DeviceName = info.szDevice,
                    FriendlyName = friendlyName,
                    DeviceId = deviceId,
                    IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0
                });
            }
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    private static (string friendlyName, string deviceId) GetDisplayDeviceInfo(string adapterDeviceName)
    {
        // First, find the adapter that matches this device name
        var adapter = new DISPLAY_DEVICE();
        adapter.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAY_DEVICE));

        uint adapterIndex = 0;
        while (EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
        {
            if (adapter.DeviceName == adapterDeviceName)
            {
                // Now enumerate the monitor(s) attached to this adapter
                var monitor = new DISPLAY_DEVICE();
                monitor.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DISPLAY_DEVICE));

                if (EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    return (
                        string.IsNullOrEmpty(monitor.DeviceString) ? adapter.DeviceString : monitor.DeviceString,
                        monitor.DeviceID ?? ""
                    );
                }

                // Fallback: use adapter info if monitor enumeration fails
                return (adapter.DeviceString, adapter.DeviceID ?? "");
            }
            adapterIndex++;
        }

        return ("Unknown Display", "");
    }

    public static MonitorInfo? FindByStableDeviceId(string savedStableId)
    {
        if (string.IsNullOrEmpty(savedStableId)) return null;

        var monitors = GetAllMonitors();
        return monitors.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.StableDeviceId) &&
            m.StableDeviceId.Equals(savedStableId, StringComparison.OrdinalIgnoreCase));
    }

    public static MonitorInfo? FindPrimary()
    {
        return GetAllMonitors().FirstOrDefault(m => m.IsPrimary);
    }

    public static int GetMonitorCount()
    {
        return GetAllMonitors().Count;
    }
}
