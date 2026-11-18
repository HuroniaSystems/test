using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GrpDialer.Services;

/// <summary>
/// Brings a window to the foreground from the background. Windows blocks plain
/// SetForegroundWindow from inactive processes, so borrow the foreground
/// thread's input state for the call.
/// </summary>
public static class WindowFocus
{
    private const int SW_RESTORE = 9;

    public static void ForceForeground(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();

        var hwnd = new WindowInteropHelper(window).Handle;
        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero);
        uint ourThread = GetCurrentThreadId();

        if (fgThread != ourThread) AttachThreadInput(ourThread, fgThread, true);
        try
        {
            ShowWindow(hwnd, SW_RESTORE);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (fgThread != ourThread) AttachThreadInput(ourThread, fgThread, false);
        }
        window.Activate();
    }

    /// <summary>True when the focused window belongs to this app (its tray menu, a message box).</summary>
    public static bool ForegroundIsOurProcess()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
