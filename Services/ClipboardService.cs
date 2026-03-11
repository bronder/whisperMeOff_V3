using System.Runtime.InteropServices;
using System.Windows.Threading;

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
        string? result = null;
        
        // Clipboard requires STA thread - use Dispatcher
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
        {
            // Already on UI thread
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    result = System.Windows.Clipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Get clipboard error");
            }
        }
        else
        {
            // Need to invoke on UI thread
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        result = System.Windows.Clipboard.GetText();
                    }
                });
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Get clipboard error (dispatcher)");
            }
        }

        return result;
    }

    public void SetText(string text)
    {
        // Clipboard requires STA thread - use Dispatcher
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
        {
            // Already on UI thread
            try
            {
                System.Windows.Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Set clipboard error");
            }
        }
        else
        {
            // Need to invoke on UI thread
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(text);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Error(ex, "Set clipboard error (dispatcher)");
                    }
                });
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Set clipboard error (dispatcher)");
            }
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

            // Wait for window to be ready with retry mechanism
            await WaitForWindowAndPaste(windowHandle, 500);
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Paste error");
        }
    }

    /// <summary>
    /// Waits for window to be ready and then pastes using retry mechanism
    /// </summary>
    private async Task WaitForWindowAndPaste(IntPtr windowHandle, int timeoutMs = 500)
    {
        var startTime = DateTime.Now;
        const int retryDelayMs = 10;
        
        while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
        {
            if (IsWindowReady(windowHandle))
            {
                // Window is ready, paste immediately
                SimulatePaste();
                return;
            }
            await Task.Delay(retryDelayMs);
        }
        
        // Timeout reached, try paste anyway as fallback
LoggingService.Debug("Window ready timeout, attempting paste anyway");
        SimulatePaste();
    }

    /// <summary>
    /// Checks if window is in a ready state for pasting
    /// </summary>
    private bool IsWindowReady(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        
        // Check if window is visible
        if (!IsWindowVisible(hWnd)) return false;
        
        // Check if window is not minimized
        if (IsIconic(hWnd)) return false;
        
        // Check if window is responsive (not hung)
        if (!IsWindowEnabled(hWnd)) return false;
        
        return true;
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

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hWnd);
}
