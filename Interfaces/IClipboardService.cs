namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for clipboard operations and text pasting
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Get text from clipboard
    /// </summary>
    /// <returns>Clipboard text or null</returns>
    string? GetText();

    /// <summary>
    /// Set text to clipboard
    /// </summary>
    /// <param name="text">Text to set</param>
    void SetText(string text);

    /// <summary>
    /// Simulate Ctrl+V paste (useful for inserting text into other applications)
    /// </summary>
    void PasteText();
}
