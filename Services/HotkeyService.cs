using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace whisperMeOff.Services;

public class HotkeyService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int HOTKEY_ID = 9000;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private IntPtr _windowHandle;
    private HwndSource? _source;
    private string _triggerKey = "r";
    private IntPtr _previousWindow;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _keyboardProc;
    private bool _isHotkeyPressed;

    public event EventHandler? HotkeyPressed;
    public event EventHandler? HotkeyReleased;

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);

        // Set up keyboard hook for release detection
        _keyboardProc = KeyboardHookCallback;
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);

        RegisterCurrentHotkey();
    }

    public void RegisterCurrentHotkey()
    {
        // Unregister first
        UnregisterHotKey(_windowHandle, HOTKEY_ID);

        // Get virtual key code from trigger key
        var vk = GetVirtualKeyCode(_triggerKey);
        if (vk == 0) return;

        // Register Ctrl+Shift + trigger key
        if (!RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, vk))
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register hotkey: Ctrl+Shift+{_triggerKey}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Registered hotkey: Ctrl+Shift+{_triggerKey}");
        }
    }

    private uint GetVirtualKeyCode(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;

        var upperKey = key.ToUpperInvariant()[0];
        if (upperKey >= 'A' && upperKey <= 'Z')
        {
            return (uint)upperKey;
        }
        if (upperKey >= '0' && upperKey <= '9')
        {
            return (uint)upperKey;
        }

        return 0;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            uint triggerVk = GetVirtualKeyCode(_triggerKey);

            // Check if our trigger key was released
            if (wParam == (IntPtr)WM_KEYUP && vkCode == triggerVk)
            {
                if (_isHotkeyPressed)
                {
                    System.Diagnostics.Debug.WriteLine($"Hotkey released");
                    _isHotkeyPressed = false;
                    HotkeyReleased?.Invoke(this, EventArgs.Empty);
                }
            }
            // Check if our trigger key was pressed
            else if (wParam == (IntPtr)WM_KEYDOWN && vkCode == triggerVk)
            {
                // Check if Ctrl+Shift are also held
                if ((GetAsyncKeyState(0x11) & 0x8000) != 0 && (GetAsyncKeyState(0x10) & 0x8000) != 0)
                {
                    _isHotkeyPressed = true;
                }
            }
        }
        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            // Record the previous window for auto-paste
            _previousWindow = GetForegroundWindow();
            System.Diagnostics.Debug.WriteLine($"Hotkey pressed, previous window: {_previousWindow}");

            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void SetTriggerKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        // Only accept single alphanumeric characters
        var upperKey = key.ToUpperInvariant();
        if (upperKey.Length == 1 && char.IsLetterOrDigit(upperKey[0]))
        {
            _triggerKey = upperKey;
            RegisterCurrentHotkey();

            // Save to settings
            App.Settings.General.HotkeyTriggerKey = _triggerKey.ToLowerInvariant();
            App.Settings.Save();
        }
    }

    public string GetTriggerKey() => _triggerKey;

    public IntPtr GetPreviousWindow() => _previousWindow;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public void Dispose()
    {
        _source?.RemoveHook(HwndHook);
        UnregisterHotKey(_windowHandle, HOTKEY_ID);
        
        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
}
