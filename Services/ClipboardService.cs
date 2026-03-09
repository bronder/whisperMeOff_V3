using System.Runtime.InteropServices;
using System.Windows;

namespace whisperMeOff.Services;

public class ClipboardService
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public string? GetText()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                return System.Windows.Clipboard.GetText();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Get clipboard error: {ex.Message}");
        }

        return null;
    }

    public void SetText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Set clipboard error: {ex.Message}");
        }
    }

    public async Task PasteToWindow(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle == IntPtr.Zero)
            {
                // No previous window, just paste to current
                await Task.Delay(50);
                SimulatePaste();
                return;
            }

            // Bring the previous window to foreground
            SetForegroundWindow(windowHandle);
            await Task.Delay(50);

            // Simulate Ctrl+V
            SimulatePaste();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Paste error: {ex.Message}");
        }
    }

    private void SimulatePaste()
    {
        // Key down Ctrl+V
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);

        // Key up Ctrl+V
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
