using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using whisperMeOff.Services;

namespace whisperMeOff.Views;

/// <summary>
/// First-run window that introduces users to the application and helps them set up their hotkey.
/// </summary>
public partial class FirstRunWindow : Window
{
    private string _selectedHotkey = "r";
    private string _selectedTheme = "Light";
    
    public FirstRunWindow()
    {
        try
        {
            LoggingService.Info("[FirstRun] Starting FirstRunWindow initialization");
            InitializeComponent();
            
            // Load current settings into UI
            LoadCurrentSettings();
            
            // Initialize theme selection
            InitializeThemeSelection();
            
            LoggingService.Info("[FirstRun] FirstRunWindow initialized successfully");
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[FirstRun] Error during FirstRunWindow initialization");
            throw;
        }
    }
    
    private void InitializeThemeSelection()
    {
        // Get current theme from settings
        var currentTheme = App.Settings?.General?.Theme ?? "Light";
        _selectedTheme = currentTheme;
        
        // Select the current theme in the combo box
        for (int i = 0; i < ThemeComboBox.Items.Count; i++)
        {
            if (ThemeComboBox.Items[i] is System.Windows.Controls.ComboBoxItem item)
            {
                // Check if this item matches the current theme
                var themeName = GetThemeNameFromItem(item);
                if (themeName == currentTheme)
                {
                    ThemeComboBox.SelectedIndex = i;
                    break;
                }
            }
        }
        
        // Show theme description
        ApplyThemePreview(currentTheme);
    }
    
    private string GetThemeNameFromItem(System.Windows.Controls.ComboBoxItem item)
    {
        // Try to find TextBlock in the item
        if (item.Content is System.Windows.Controls.StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is System.Windows.Controls.TextBlock textBlock)
                {
                    return textBlock.Text;
                }
            }
        }
        // Fallback
        return item.Content?.ToString() ?? "Light";
    }
    
    private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            var themeName = GetThemeNameFromItem(item);
            _selectedTheme = themeName;
            ApplyThemePreview(themeName);
        }
    }
    
    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        // Close the window without saving
        DialogResult = false;
        Close();
    }
    
    private void ApplyThemePreview(string themeName)
    {
        try
        {
            // Just show theme description - don't apply during FirstRun
            // The theme will be applied when MainWindow loads
            var description = themeName switch
            {
                "Light" => "✓ Light theme selected - Clean white background with blue accents",
                "Dark" => "✓ Dark theme selected - Dark gray background with blue accents",
                "Nord" => "✓ Nord theme selected - Cool arctic blue with teal accents",
                "Dracula" => "✓ Dracula theme selected - Dark purple with pink accents",
                "Gruvbox" => "✓ Gruvbox theme selected - Retro warm browns with green accents",
                "Monokai" => "✓ Monokai theme selected - Dark with vibrant colors",
                "Synthwave" => "✓ Synthwave theme selected - Retro neon pink and purple",
                _ => $"✓ {themeName} theme selected"
            };
            ThemePreviewText.Text = description;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, $"[FirstRun] Error in theme preview: {themeName}");
            ThemePreviewText.Text = $"Theme: {themeName}";
        }
    }
    
    private void LoadCurrentSettings()
    {
        try
        {
            // Load current hotkey from settings
            var currentKey = "R";
            if (App.Settings?.General?.HotkeyTriggerKey != null)
            {
                currentKey = App.Settings.General.HotkeyTriggerKey.ToUpperInvariant();
            }
            _selectedHotkey = currentKey.ToLowerInvariant();
            
            // Update the hotkey textbox
            if (HotkeyTextBox != null)
            {
                HotkeyTextBox.Text = currentKey;
            }
            
            UpdateHotkeyDisplay();
            
            // Load other settings with null checks
            if (App.Settings?.General != null)
            {
                LaunchAtLoginCheckBox.IsChecked = App.Settings.General.LaunchAtLogin;
                MinimizeToTrayCheckBox.IsChecked = App.Settings.General.MinimizeToTray;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[FirstRun] Error loading current settings");
            // Use defaults on error
            _selectedHotkey = "r";
            if (HotkeyTextBox != null)
            {
                HotkeyTextBox.Text = "R";
            }
        }
    }
    
    private void HotkeyTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (HotkeyTextBox == null || CurrentHotkeyText == null) return;
        
        // Get the typed letter and convert to uppercase
        string text = HotkeyTextBox.Text.ToUpperInvariant();
        
        // Only accept single letter
        if (!string.IsNullOrEmpty(text) && text.Length == 1 && char.IsLetter(text[0]))
        {
            _selectedHotkey = text.ToLowerInvariant();
            UpdateHotkeyDisplay();
        }
    }
    
    private void UpdateHotkeyDisplay()
    {
        // Skip if control not yet initialized
        if (CurrentHotkeyText == null) return;
        
        CurrentHotkeyText.Text = $"Press Ctrl+Shift+{_selectedHotkey.ToUpperInvariant()} to record";
    }
    
    private void GetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        // Save the hotkey setting
        App.Settings.General.HotkeyTriggerKey = _selectedHotkey;
        
        // Save the theme setting
        App.Settings.General.Theme = _selectedTheme;
        
        // Save other optional settings
        App.Settings.General.LaunchAtLogin = LaunchAtLoginCheckBox.IsChecked ?? false;
        App.Settings.General.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked ?? false;
        
        // Mark onboarding as completed
        App.Settings.General.HasCompletedOnboarding = true;
        
        // Save all settings
        App.Settings.Save();
        
        // Note: Hotkey will be registered when MainWindow loads
        // We don't call App.Hotkey.SetTriggerKey here because Hotkey.Initialize 
        // requires a Window handle which isn't available during onboarding
        LoggingService.Info("[FirstRun] Onboarding completed, hotkey set to: " + _selectedHotkey.ToUpperInvariant());
        LoggingService.Info("[FirstRun] Theme selected: " + _selectedTheme);
        LoggingService.Info("[FirstRun] Hotkey will be registered when main window loads");
        
        // Handle launch at login setting
        if (App.Settings.General.LaunchAtLogin)
        {
            RegisterLaunchAtLogin();
        }
        else
        {
            UnregisterLaunchAtLogin();
        }
        
        // Close this window and the app will show the main window
        DialogResult = true;
        Close();
    }
    
    private void RegisterLaunchAtLogin()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue("whisperMeOff", $"\"{exePath}\"");
                    LoggingService.Info("[FirstRun] Registered for launch at login");
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[FirstRun] Failed to register launch at login");
        }
    }
    
    private void UnregisterLaunchAtLogin()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                key.DeleteValue("whisperMeOff", false);
                LoggingService.Info("[FirstRun] Unregistered from launch at login");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[FirstRun] Failed to unregister launch at login");
        }
    }
    
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Don't allow closing without completing setup - user must set a hotkey
        // Actually, let's allow closing but warn them
        base.OnClosing(e);
    }
}
