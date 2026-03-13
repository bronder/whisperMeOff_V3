using System.Windows;

namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for global hotkey registration and keyboard monitoring
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Current trigger key for hotkey
    /// </summary>
    string TriggerKey { get; set; }

    /// <summary>
    /// Raised when the hotkey is pressed
    /// </summary>
    event EventHandler? HotkeyPressed;

    /// <summary>
    /// Raised when the hotkey is released
    /// </summary>
    event EventHandler? HotkeyReleased;

    /// <summary>
    /// Initialize the hotkey service with a window
    /// </summary>
    /// <param name="window">Window to use for message handling</param>
    void Initialize(Window window);

    /// <summary>
    /// Register the current hotkey configuration
    /// </summary>
    void RegisterCurrentHotkey();
}
