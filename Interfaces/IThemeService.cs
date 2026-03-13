namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for managing application themes
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Currently applied theme name
    /// </summary>
    string CurrentTheme { get; }

    /// <summary>
    /// Apply a theme by name
    /// </summary>
    void ApplyTheme(string themeName);

    /// <summary>
    /// Get list of available theme names
    /// </summary>
    string[] GetAvailableThemes();

    /// <summary>
    /// Get the index of the current theme
    /// </summary>
    int GetCurrentThemeIndex();

    /// <summary>
    /// Set theme by index
    /// </summary>
    void SetThemeByIndex(int index);
}
