using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace whisperMeOff.Services;

/// <summary>
/// Service for managing application settings with persistence to JSON.
/// Handles loading, saving, and encryption of sensitive data.
/// </summary>
/// <remarks>
/// Settings are stored in %APPDATA%/whisperMeOff/settings.json.
/// Sensitive data like API tokens are encrypted using Windows DPAPI.
/// </remarks>
public class SettingsService
{
    /// <summary>
    /// Whisper transcription settings.
    /// </summary>
    public WhisperSettings Whisper { get; set; } = new();

    /// <summary>
    /// LLama model settings.
    /// </summary>
    public LlamaSettings Llama { get; set; } = new();

    /// <summary>
    /// Audio input settings.
    /// </summary>
    public AudioSettings Audio { get; set; } = new();

    /// <summary>
    /// General application settings.
    /// </summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>
    /// Loads settings from the JSON file, or creates default settings if file doesn't exist.
    /// </summary>
    /// <remarks>
    /// Decrypts sensitive data (HuggingFace tokens) after loading.
    /// </remarks>
    public void Load()
    {
        try
        {
            if (File.Exists(App.SettingsPath))
            {
                var json = File.ReadAllText(App.SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    Whisper = settings.Whisper ?? new WhisperSettings();
                    Llama = settings.Llama ?? new LlamaSettings();
                    Audio = settings.Audio ?? new AudioSettings();
                    General = settings.General ?? new GeneralSettings();
                    
                    // Decrypt sensitive data
                    var beforeDecrypt = Llama.HuggingFaceToken;
                    Llama.HuggingFaceToken = Decrypt(Llama.HuggingFaceToken);
                    
                    LoggingService.Debug($"Settings loaded - Llama Token length: {Llama.HuggingFaceToken?.Length ?? 0} chars");
                    LoggingService.Debug($"Settings loaded from {App.SettingsPath}");
                    LoggingService.Debug($"Llama ModelId: {Llama.ModelId}");
                }
            }
            else
            {
                LoggingService.Info($"No settings file at {App.SettingsPath} - using defaults");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to load settings - using defaults");
        }

        // Set defaults
        if (string.IsNullOrEmpty(General.HotkeyTriggerKey))
            General.HotkeyTriggerKey = "r";
    }

    /// <summary>
    /// Saves current settings to the JSON file.
    /// </summary>
    /// <remarks>
    /// Encrypts sensitive data (HuggingFace tokens) before saving.
    /// Skips re-encryption if token already appears to be encrypted.
    /// </remarks>
    public void Save()
    {
        try
        {
            var settings = new AppSettings
            {
                Whisper = Whisper,
                Llama = Llama,
                Audio = Audio,
                General = General
            };

            // Encrypt sensitive data before saving (but only if it looks like plain text, not already encrypted)
            if (!string.IsNullOrEmpty(settings.Llama.HuggingFaceToken))
            {
                var token = settings.Llama.HuggingFaceToken;
                
                // If the token already looks like it's been encrypted (very long base64), don't re-encrypt
                // This prevents the double-encryption bug
                if (token.Length > 200 && IsBase64String(token))
                {
                    LoggingService.Debug("Token already encrypted, skipping encryption");
                }
                else
                {
                    var beforeEncrypt = token;
                    settings.Llama.HuggingFaceToken = Encrypt(token);
                    LoggingService.Debug($"Token encrypted: {beforeEncrypt?.Length ?? 0} -> {settings.Llama.HuggingFaceToken?.Length ?? 0} chars");
                }
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(App.SettingsPath, json);
            LoggingService.Info($"Settings saved to {App.SettingsPath}");
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to save settings");
        }
    }

    /// <summary>
    /// Encrypts a string using Windows DPAPI (user-specific)
    /// </summary>
    private static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"[Settings] Encryption error: {ex.Message}");
            return plainText; // Return plain text if encryption fails
        }
    }

    /// <summary>
    /// Decrypts a string using Windows DPAPI (public for external use)
    /// </summary>
    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        try
        {
            // Check if it's encrypted (starts with base64 indicator)
            var plainBytes = Convert.FromBase64String(encryptedText);
            var decryptedBytes = ProtectedData.Unprotect(plainBytes, null, DataProtectionScope.CurrentUser);
            var result = Encoding.UTF8.GetString(decryptedBytes);
            LoggingService.Debug($"[Decrypt] Success! {encryptedText.Length} -> {result.Length} chars");
            return result;
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"[Decrypt] Failed: {ex.Message}, returning as-is ({encryptedText.Length} chars)");
            return encryptedText;
        }
    }

    /// <summary>
    /// Checks if a string is valid Base64 (used to detect already-encrypted tokens)
    /// </summary>
    private static bool IsBase64String(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;

        // Base64 strings only contain A-Z, a-z, 0-9, +, /, and = for padding
        // They also tend to be longer (encrypted tokens are much longer than plain text)
        if (s.Length < 20)
            return false;

        try
        {
            Convert.FromBase64String(s);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class AppSettings
{
    public WhisperSettings? Whisper { get; set; }
    public LlamaSettings? Llama { get; set; }
    public AudioSettings? Audio { get; set; }
    public GeneralSettings? General { get; set; }
}

public class WhisperSettings
{
    public string ModelPath { get; set; } = "";
    public string ModelSize { get; set; } = "medium";
    public string Language { get; set; } = "auto";
    public bool Translate { get; set; } = false;
    public string CustomVocabulary { get; set; } = "";
    public List<WordReplacement> WordReplacements { get; set; } = new();
}

public class WordReplacement
{
    public string Source { get; set; } = "";
    public string Replacement { get; set; } = "";
}

public class LlamaSettings
{
    public bool Enabled { get; set; } = false;
    public bool Translate { get; set; } = false;
    public string TranslateTo { get; set; } = "en";
    public string ModelPath { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string HuggingFaceToken { get; set; } = "";
}

public class AudioSettings
{
    public string DeviceId { get; set; } = "";
}

public class GeneralSettings
{
    public string Theme { get; set; } = "Light";
    public string HotkeyTriggerKey { get; set; } = "r";
    public bool LaunchAtLogin { get; set; } = false;
    public string ModelDownloadPath { get; set; } = "";
    public string LlamaDownloadPath { get; set; } = "";
    public bool RestoreClipboard { get; set; } = true;
    public int ClipboardRestoreDelayMs { get; set; } = 1000;
    public bool PushToTalkMode { get; set; } = true; // true = hold to talk, false = toggle
    public bool MinimizeToTray { get; set; } = false; // minimize to system tray instead of taskbar
}
