using System.Windows;

namespace whisperMeOff.Services;

using Application = System.Windows.Application;

public class ThemeService
{
    private const string ThemeFolder = "Themes";
    private static readonly string[] AvailableThemes = { "Light", "Dark", "Nord", "Dracula", "Gruvbox", "Monokai", "Synthwave" };
    
    public string CurrentTheme { get; private set; } = "Light";
    
    /// <summary>
    /// Applies the specified theme to the application
    /// </summary>
    /// <param name="themeName">Theme name (Light or Dark)</param>
    public void ApplyTheme(string themeName)
    {
        if (!AvailableThemes.Contains(themeName))
            themeName = "Light";
            
        var app = Application.Current;
        var resources = app.Resources.MergedDictionaries;
        
        // Remove existing theme dictionaries
        var toRemove = resources
            .Where(d => d.Source?.OriginalString.Contains(ThemeFolder) == true)
            .ToList();
            
        foreach (var dict in toRemove)
            resources.Remove(dict);
            
        // Add new theme with exception handling
        try
        {
            var newTheme = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{ThemeFolder}/{themeName}.xaml")
            };
            resources.Add(newTheme);
            CurrentTheme = themeName;
            System.Diagnostics.Debug.WriteLine($"[Theme] Applied: {themeName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Theme] Failed to load theme '{themeName}': {ex.Message}");
            // If we failed to load the requested theme, try to fall back to Light
            if (themeName != "Light")
            {
                try
                {
                    var fallbackTheme = new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/{ThemeFolder}/Light.xaml")
                    };
                    resources.Add(fallbackTheme);
                    CurrentTheme = "Light";
                    System.Diagnostics.Debug.WriteLine("[Theme] Fallback to Light theme successful");
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Theme] Critical failure - could not load fallback theme: {fallbackEx.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// Gets the list of available theme names
    /// </summary>
    public string[] GetAvailableThemes() => AvailableThemes;
    
    /// <summary>
    /// Gets the index of the current theme in the available themes array
    /// </summary>
    public int GetCurrentThemeIndex()
    {
        for (int i = 0; i < AvailableThemes.Length; i++)
        {
            if (AvailableThemes[i].Equals(CurrentTheme, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }
    
    /// <summary>
    /// Sets the theme by index in the available themes array
    /// </summary>
    public void SetThemeByIndex(int index)
    {
        if (index >= 0 && index < AvailableThemes.Length)
        {
            ApplyTheme(AvailableThemes[index]);
        }
    }
}
