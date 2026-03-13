using System.Security.Cryptography;
using System.Text;

namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for managing application settings
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Load settings from disk
    /// </summary>
    void Load();

    /// <summary>
    /// Save settings to disk
    /// </summary>
    void Save();

    /// <summary>
    /// Whisper transcription settings
    /// </summary>
    WhisperSettings Whisper { get; }

    /// <summary>
    /// LLama model settings
    /// </summary>
    LlamaSettings Llama { get; }

    /// <summary>
    /// Audio recording settings
    /// </summary>
    AudioSettings Audio { get; }

    /// <summary>
    /// General application settings
    /// </summary>
    GeneralSettings General { get; }

    /// <summary>
    /// Decrypt encrypted text
    /// </summary>
    static string Decrypt(string encryptedText)
    {
        try
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
            byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Whisper transcription settings
/// </summary>
public class WhisperSettings
{
    public string ModelPath { get; set; } = string.Empty;
    public string ModelSize { get; set; } = "medium";
    public string Language { get; set; } = "auto";
    public bool Translate { get; set; }
    public bool EnableWordReplacements { get; set; }
    public List<WordReplacement> WordReplacements { get; set; } = new();
    public string CustomVocabulary { get; set; } = string.Empty;
}

/// <summary>
/// LLama model settings
/// </summary>
public class LlamaSettings
{
    public string ModelPath { get; set; } = string.Empty;
    public int ContextSize { get; set; } = 2048;
    public int GpuLayerCount { get; set; } = 35;
    public bool EnableFormatting { get; set; }
    public bool EnableTranslation { get; set; }
    public string TranslationTargetLanguage { get; set; } = "en";
}

/// <summary>
/// Audio recording settings
/// </summary>
public class AudioSettings
{
    public string SelectedDeviceId { get; set; } = string.Empty;
    public bool EnableNoiseReduction { get; set; }
    public int NoiseReductionLevel { get; set; }
    public bool EnablePunctuation { get; set; }
}

/// <summary>
/// General application settings
/// </summary>
public class GeneralSettings
{
    public string Theme { get; set; } = "Dark";
    public bool LaunchAtLogin { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool StartRecordingOnLaunch { get; set; }
    public bool AutoPaste { get; set; }
    public string HotkeyTriggerKey { get; set; } = "F9";
    public bool AutoSaveTranscriptions { get; set; }
    public int TranscriptionHistoryLimit { get; set; } = 50;
    public bool AutoDeleteOldTranscriptions { get; set; }
    public int AutoDeleteDays { get; set; }
}

/// <summary>
/// Word replacement rule
/// </summary>
public class WordReplacement
{
    public string Source { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
}
