using static Snapline.Interop.Native;

namespace Snapline.Interop;

/// <summary>Foreground full-screen windows on the line's monitor, excluding ordinary maximized windows.</summary>
public static class FullScreen
{
    /// <summary>Own window registry also used by capture origin inference.</summary>
    public static readonly HashSet<IntPtr> Own = new();
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    public static bool IsBorderlessCover(RECT r, RECT bounds, long style) =>
        (style & (WS_CAPTION | WS_THICKFRAME)) == 0 &&
        r.Left <= bounds.Left && r.Top <= bounds.Top && r.Right >= bounds.Right && r.Bottom >= bounds.Bottom;

    public static bool IsActive(Display monitor)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out var pid);
        // Check actual ownership, not a stale/reused handle. Settings and editors never block the line.
        if (pid == Environment.ProcessId) return false;
        if (MonitorFromWindow(fg, MONITOR_DEFAULTTONULL) != monitor.Handle) return false;
        if (ShellClasses.Contains(ClassNameOf(fg)) || IsIconic(fg) || IsCloaked(fg)) return false;
        if (!GetWindowRect(fg, out var r)) return false;
        long style = GetWindowLongPtr(fg, GWL_STYLE).ToInt64();
        // IsZoomed only means maximized. A borderless full-screen app may also be zoomed.
        if (IsBorderlessCover(r, monitor.Bounds, style)) return true;
        // Exclusive D3D games can retain framed window styles. Trust the explicit
        // exclusive full-screen state before rejecting those styles; presentation
        // notification settings alone never prove full-screen content.
        return SHQueryUserNotificationState(out var state) == 0 &&
               state == QUERY_USER_NOTIFICATION_STATE.RunningD3dFullScreen;
    }
}
