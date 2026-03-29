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
    /// Text transformation settings.
    /// </summary>
    public TransformationSettings Transformation { get; set; } = new();

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
                    Transformation = settings.Transformation ?? new TransformationSettings();
                    
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
                General = General,
                Transformation = Transformation
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
                    try
                    {
                        var beforeEncrypt = token;
                        settings.Llama.HuggingFaceToken = Encrypt(token);
                        LoggingService.Debug($"Token encrypted: {beforeEncrypt?.Length ?? 0} -> {settings.Llama.HuggingFaceToken?.Length ?? 0} chars");
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Encryption failed - do not save the token in plain text
                        LoggingService.Error($"[Settings] Failed to encrypt token - clearing token to prevent exposure: {ex.Message}");
                        settings.Llama.HuggingFaceToken = string.Empty;
                    }
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
            // Log as ERROR - do NOT silently return plain text as it exposes credentials
            LoggingService.Error($"[Settings] Encryption failed - token will not be stored: {ex.Message}");
            throw new InvalidOperationException("Failed to encrypt sensitive data. Token will not be saved.", ex);
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
            // Check if it's valid base64 - if not, it's likely plain text (not encrypted)
            if (!IsBase64String(encryptedText))
            {
                LoggingService.Debug($"[Decrypt] Text is not base64 encoded, returning as-is (likely plain text)");
                return encryptedText;
            }
            
            var plainBytes = Convert.FromBase64String(encryptedText);
            var decryptedBytes = ProtectedData.Unprotect(plainBytes, null, DataProtectionScope.CurrentUser);
            var result = Encoding.UTF8.GetString(decryptedBytes);
            LoggingService.Debug($"[Decrypt] Success! {encryptedText.Length} -> {result.Length} chars");
            return result;
        }
        catch (Exception ex)
        {
            // Decryption failed - could be corrupted data or from different user profile
            // Log as error and return empty to prevent potential data leakage
            LoggingService.Error($"[Decrypt] Failed - data may be corrupted or from different user: {ex.Message}");
            return string.Empty;
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
    public TransformationSettings? Transformation { get; set; }
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
    public bool CaseSensitive { get; set; } = false;
    public bool IsRegex { get; set; } = false;
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
    public bool RestoreClipboard { get; set; } = false;
    public int ClipboardRestoreDelayMs { get; set; } = 1000;
    public bool PushToTalkMode { get; set; } = true; // true = hold to talk, false = toggle
    public bool MinimizeToTray { get; set; } = false; // minimize to system tray instead of taskbar
    public bool HasCompletedOnboarding { get; set; } = false; // tracks if first-run wizard was completed
    public bool DownloadPathsExpanded { get; set; } = false; // tracks expander state
    public bool PreRecordingBuffer { get; set; } = true; // capture 300ms before trigger to avoid clipping
}

public class TransformationSettings
{
    public bool EnableAutoTransform { get; set; } = false;
    public bool ShowTransformUI { get; set; } = false;
    public string DefaultProfileId { get; set; } = "";
    public string DefaultType { get; set; } = "Grammar";
    public string DefaultDirection { get; set; } = "Default";
    public bool PreserveProperNouns { get; set; } = true;
    public bool PreserveTechnicalTerms { get; set; } = true;
    public bool EnableQualityScoring { get; set; } = true;
    public bool EnableBatchProcessing { get; set; } = false;
    public int BatchSize { get; set; } = 5;
    public double Temperature { get; set; } = AppConstants.DefaultTemperature;
    public int MaxTokens { get; set; } = AppConstants.DefaultMaxTokens;
    public int MaxTextLength { get; set; } = AppConstants.DefaultMaxTextLength;
    public bool AutoSaveHistory { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = AppConstants.DefaultHistoryRetentionDays;
    
    // Custom prompts for formal/informal transformations
    public string CustomFormalPrompt { get; set; } = "";
    public string CustomInformalPrompt { get; set; } = "";
    public string CustomCreativePrompt { get; set; } = "";
    public string CustomHumorPrompt { get; set; } = "";
}
