using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GrpDialer.Services;

/// <summary>
/// System-wide low-level keyboard hook that fires on numpad Enter only
/// (VK_RETURN with the extended-key flag). The main Enter key is untouched.
/// While another app has focus the keystroke is swallowed; while our own
/// window has focus it passes through so WPF handles it normally.
/// </summary>
public sealed class NumpadEnterHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int VK_RETURN = 0x0D;
    private const int LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10;

    private readonly LowLevelKeyboardProc _proc; // kept alive: GC would kill the callback
    private readonly Func<bool> _isOwnWindowActive;
    private IntPtr _hook;
    private bool _swallowingKeyUp;

    public event Action? Pressed;

    public NumpadEnterHook(Func<bool> isOwnWindowActive)
    {
        _isOwnWindowActive = isOwnWindowActive;
        _proc = HookProc;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        using var module = Process.GetCurrentProcess().MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool numpadEnter = info.vkCode == VK_RETURN &&
                               (info.flags & LLKHF_EXTENDED) != 0 &&
                               (info.flags & LLKHF_INJECTED) == 0;
            if (numpadEnter)
            {
                int msg = wParam.ToInt32();
                if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && !_isOwnWindowActive())
                {
                    _swallowingKeyUp = true;
                    Pressed?.Invoke();
                    return (IntPtr)1;
                }
                if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _swallowingKeyUp)
                {
                    _swallowingKeyUp = false;
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
