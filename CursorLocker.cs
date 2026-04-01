using System.Runtime.InteropServices;
using static MouseLock.NativeMethods;

namespace MouseLock;

internal sealed class CursorLocker : IDisposable
{
    private RECT _lockRect;
    private bool _isLocking;
    private IntPtr _mouseHookHandle;
    private LowLevelMouseProc? _mouseHookDelegate; // prevent GC
    private System.Windows.Forms.Timer? _pollingTimer;
    private int _hookReinstallCounter;
    private const int HookReinstallIntervalTicks = 200; // ~30s at 150ms interval
    private bool _disposed;

    public bool IsLocking => _isLocking;

    public void StartLocking(RECT monitorRect)
    {
        _lockRect = monitorRect;
        _isLocking = true;

        // Layer 1: ClipCursor
        ClipCursor(ref _lockRect);

        // Layer 2: Low-level mouse hook
        InstallMouseHook();

        // Layer 3: Polling timer
        StartPollingTimer();

        // Snap cursor back if it's currently outside the lock rect
        SnapCursorIfNeeded();
    }

    public void StopLocking()
    {
        _isLocking = false;

        // Release ClipCursor
        ClipCursor(IntPtr.Zero);

        // Remove mouse hook
        UninstallMouseHook();

        // Stop timer
        StopPollingTimer();
    }

    public void UpdateLockRect(RECT newRect)
    {
        _lockRect = newRect;
        if (_isLocking)
        {
            ClipCursor(ref _lockRect);
            SnapCursorIfNeeded();
        }
    }

    // --- Layer 2: Mouse Hook ---

    private void InstallMouseHook()
    {
        UninstallMouseHook(); // clean up any existing hook

        _mouseHookDelegate = MouseHookCallback;
        IntPtr moduleHandle = GetModuleHandle("user32");
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookDelegate, moduleHandle, 0);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isLocking)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_MOUSEMOVE)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Skip injected events to avoid infinite loop with our own SetCursorPos calls
                if ((hookStruct.flags & LLMHF_INJECTED) != 0)
                    return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);

                int x = hookStruct.pt.X;
                int y = hookStruct.pt.Y;

                if (x < _lockRect.Left || x >= _lockRect.Right ||
                    y < _lockRect.Top || y >= _lockRect.Bottom)
                {
                    int clampedX = Math.Clamp(x, _lockRect.Left, _lockRect.Right - 1);
                    int clampedY = Math.Clamp(y, _lockRect.Top, _lockRect.Bottom - 1);
                    SetCursorPos(clampedX, clampedY);

                    // Block the original out-of-bounds movement
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    // --- Layer 3: Polling Timer ---

    private void StartPollingTimer()
    {
        StopPollingTimer();
        _hookReinstallCounter = 0;

        _pollingTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _pollingTimer.Tick += PollingTimerTick;
        _pollingTimer.Start();
    }

    private void StopPollingTimer()
    {
        if (_pollingTimer != null)
        {
            _pollingTimer.Stop();
            _pollingTimer.Dispose();
            _pollingTimer = null;
        }
    }

    private void PollingTimerTick(object? sender, EventArgs e)
    {
        if (!_isLocking) return;

        // Re-apply ClipCursor if another app cleared it
        if (GetClipCursor(out RECT currentClip))
        {
            if (currentClip.Left != _lockRect.Left || currentClip.Top != _lockRect.Top ||
                currentClip.Right != _lockRect.Right || currentClip.Bottom != _lockRect.Bottom)
            {
                ClipCursor(ref _lockRect);
            }
        }

        // Snap cursor back if it escaped
        SnapCursorIfNeeded();

        // Periodically reinstall the hook to guard against silent removal
        _hookReinstallCounter++;
        if (_hookReinstallCounter >= HookReinstallIntervalTicks)
        {
            _hookReinstallCounter = 0;
            InstallMouseHook();
        }
    }

    private void SnapCursorIfNeeded()
    {
        if (GetCursorPos(out POINT pos))
        {
            if (pos.X < _lockRect.Left || pos.X >= _lockRect.Right ||
                pos.Y < _lockRect.Top || pos.Y >= _lockRect.Bottom)
            {
                int clampedX = Math.Clamp(pos.X, _lockRect.Left, _lockRect.Right - 1);
                int clampedY = Math.Clamp(pos.Y, _lockRect.Top, _lockRect.Bottom - 1);
                SetCursorPos(clampedX, clampedY);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopLocking();
        _mouseHookDelegate = null;
    }
}
