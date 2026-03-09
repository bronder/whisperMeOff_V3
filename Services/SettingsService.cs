using System.IO;
using System.Text.Json;

namespace whisperMeOff.Services;

public class SettingsService
{
    public WhisperSettings Whisper { get; set; } = new();
    public LlamaSettings Llama { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public GeneralSettings General { get; set; } = new();

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
                }
            }
        }
        catch
        {
            // Use defaults on error
        }

        // Set defaults
        if (string.IsNullOrEmpty(General.HotkeyTriggerKey))
            General.HotkeyTriggerKey = "r";
    }

    public void Save()
    {
        var settings = new AppSettings
        {
            Whisper = Whisper,
            Llama = Llama,
            Audio = Audio,
            General = General
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(App.SettingsPath, json);
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
}

public class LlamaSettings
{
    public bool Enabled { get; set; } = false;
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
    public string HotkeyTriggerKey { get; set; } = "r";
    public bool LaunchAtLogin { get; set; } = false;
    public string ModelDownloadPath { get; set; } = "";
    public string LlamaDownloadPath { get; set; } = "";
}
