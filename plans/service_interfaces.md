# Service Interface Definitions for DI Refactoring

This document provides complete interface definitions extracted from the existing service classes.
A coding agent can use these interfaces to implement proper Dependency Injection.

---

## 1. ISettingsService

**Purpose**: Manages application settings persistence

**Location**: [`Services/SettingsService.cs`](Services/SettingsService.cs)

**Interface Definition**:

```csharp
public interface ISettingsService
{
    void Load();
    void Save();
    
    WhisperSettings Whisper { get; }
    LlamaSettings Llama { get; }
    AudioSettings Audio { get; }
    GeneralSettings General { get; }
    
    static string Decrypt(string encryptedText);
}
```

**Nested Types** (used by settings classes):

```csharp
public class WhisperSettings
{
    public string ModelPath { get; set; }
    public string ModelSize { get; set; }  // "tiny", "base", "small", "medium", "large"
    public string Language { get; set; }  // "auto" or language code
    public bool Translate { get; set; }
    public bool EnableWordReplacements { get; set; }
    public List<WordReplacement> WordReplacements { get; set; }
    public string CustomVocabulary { get; set; }
}

public class LlamaSettings
{
    public string ModelPath { get; set; }
    public int ContextSize { get; set; }
    public int GpuLayerCount { get; set; }
    public bool EnableFormatting { get; set; }
    public bool EnableTranslation { get; set; }
    public string TranslationTargetLanguage { get; set; }
}

public class AudioSettings
{
    public string SelectedDeviceId { get; set; }
    public bool EnableNoiseReduction { get; set; }
    public int NoiseReductionLevel { get; set; }
    public bool EnablePunctuation { get; set; }
}

public class GeneralSettings
{
    public string Theme { get; set; }
    public bool LaunchAtLogin { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool StartRecordingOnLaunch { get; set; }
    public bool AutoPaste { get; set; }
    public string HotkeyTriggerKey { get; set; }
    public bool AutoSaveTranscriptions { get; set; }
    public int TranscriptionHistoryLimit { get; set; }
    public bool AutoDeleteOldTranscriptions { get; set; }
    public int AutoDeleteDays { get; set; }
}

public class WordReplacement
{
    public string Source { get; set; }
    public string Replacement { get; set; }
}
```

---

## 2. IThemeService

**Purpose**: Manages application theming

**Location**: [`Services/ThemeService.cs`](Services/ThemeService.cs)

**Interface Definition**:

```csharp
public interface IThemeService
{
    string CurrentTheme { get; }
    
    void ApplyTheme(string themeName);
    string[] GetAvailableThemes();
    int GetCurrentThemeIndex();
    void SetThemeByIndex(int index);
}
```

**Available Themes**: Dark, Dracula, Gruvbox, Light, Monokai, Nord, Synthwave

---

## 3. IAudioService

**Purpose**: Handles audio recording from microphone

**Location**: [`Services/AudioService.cs`](Services/AudioService.cs)

**Interface Definition**:

```csharp
public interface IAudioService : IDisposable
{
    bool IsRecording { get; }
    double LastRecordingDuration { get; }
    
    event EventHandler? RecordingStarted;
    event EventHandler? RecordingStopped;
    event EventHandler<double>? AudioLevelChanged;
    
    List<AudioDeviceInfo> GetAvailableDevices();
    void StartRecording();
    Task<string?> StopRecordingAsync();
}

public class AudioDeviceInfo
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int Channels { get; set; }
    public int sampleRate { get; set; }
}
```

**Key Implementation Details**:
- Uses NAudio library (v2.2.1)
- Records at 16kHz, 16-bit, mono (PCM)
- Returns WAV file path in temp directory
- Must call `StopRecordingAsync()` to get the audio file
- Caller is responsible for cleaning up temporary file

---

## 4. IWhisperService

**Purpose**: Speech-to-text transcription using Whisper model

**Location**: [`Services/WhisperService.cs`](Services/WhisperService.cs)

**Interface Definition**:

```csharp
public interface IWhisperService : IDisposable
{
    bool IsInitialized { get; }
    
    Task InitializeAsync();
    string GetModelPath();
    Task<string> TranscribeAsync(string audioPath);
    void ReloadModel(string modelPath);
}
```

**Key Implementation Details**:
- Uses Whisper.net 1.9.1-preview1
- Depends on `ISettingsService` for configuration (language, custom vocabulary, word replacements)
- **CRITICAL ISSUE #3**: Lines 152-189 use unsafe reflection to enable translation:
  - Tries to find `WithTask` method on builder
  - Searches for `WhisperTask` enum at runtime
  - This is fragile and may break between versions

---

## 5. ILlamaService

**Purpose**: Text formatting and translation using LLama model

**Location**: [`Services/LlamaService.cs`](Services/LlamaService.cs)

**Interface Definition**:

```csharp
public interface ILlamaService : IDisposable
{
    bool IsLoaded { get; }
    string? ModelPath { get; }
    
    event EventHandler<bool>? ModelLoaded;
    
    Task InitializeAsync(string modelPath);
    Task<string> FormatTextAsync(string rawText);
    Task<string> TranslateTextAsync(string rawText, string targetLanguage = "en");
}
```

**Key Implementation Details**:
- Uses LLamaSharp v0.26.0
- Loads GGUF format models
- FormatTextAsync: Corrects spelling/grammar with temperature=0
- TranslateTextAsync: Supports 26+ languages with name mapping
- Has specific error handling for Gemma models (not supported)

---

## 6. IDatabaseService

**Purpose**: SQLite database for transcription history

**Location**: [`Services/DatabaseService.cs`](Services/DatabaseService.cs)

**Interface Definition**:

```csharp
public interface IDatabaseService : IDisposable
{
    Task InitializeAsync();
    Task<long> AddTranscriptionAsync(string text, double duration, string? model, string? language);
    Task<List<TranscriptionRecord>> GetTranscriptionsAsync(int limit = 50);
    Task<TranscriptionRecord?> GetTranscriptionAsync(long id);
    Task<bool> DeleteTranscriptionAsync(long id);
    Task<bool> ClearAllTranscriptionsAsync();
    Task<int> ClearTranscriptionsOlderThanAsync(TimeSpan olderThan);
    Task<List<TranscriptionRecord>> SearchTranscriptionsAsync(string query);
}

public class TranscriptionRecord
{
    public long Id { get; set; }
    public string Text { get; set; }
    public string Timestamp { get; set; }
    public double? Duration { get; set; }
    public string? Model { get; set; }
    public string? Language { get; set; }
}
```

**Key Implementation Details**:
- Uses Microsoft.Data.Sqlite
- Database file: `transcriptions.db` in AppData
- Table: `transcriptions` (id, text, timestamp, duration, model, language)

---

## 7. IHotkeyService

**Purpose**: Global hotkey registration and handling

**Location**: [`Services/HotkeyService.cs`](Services/HotkeyService.cs)

**Interface Definition**:

```csharp
public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    event EventHandler? HotkeyReleased;
    
    void Initialize(Window window);
    void RegisterCurrentHotkey();
    void SetTriggerKey(string key);
    string GetTriggerKey();
    IntPtr GetPreviousWindow();
}
```

**Key Implementation Details**:
- Uses Windows API (user32.dll) for global keyboard hooks
- Default trigger key: configurable via settings
- Uses Ctrl+Shift + trigger key combination
- Records previous foreground window for auto-paste functionality

---

## 8. IClipboardService

**Purpose**: Clipboard operations and window pasting

**Location**: [`Services/ClipboardService.cs`](Services/ClipboardService.cs)

**Interface Definition**:

```csharp
public interface IClipboardService
{
    string? GetText();
    void SetText(string text);
    Task PasteToWindow(IntPtr windowHandle);
}
```

**Key Implementation Details**:
- Uses Windows API for clipboard and window manipulation
- Uses SendInput for paste simulation
- Small delay (50ms) before paste to ensure window focus

---

## 9. IModelDownloadService

**Purpose**: Download Whisper and LLama models

**Location**: [`Services/ModelDownloadService.cs`](Services/ModelDownloadService.cs)

**Interface Definition**:

```csharp
public interface IModelDownloadService : IDisposable
{
    event EventHandler<DownloadProgressEventArgs>? DownloadProgress;
    
    Task<string?> DownloadWhisperModelAsync(string size, IProgress<double>? progress);
    Task<string?> DownloadLlamaModelAsync(string modelId, IProgress<double>? progress);
}

public class DownloadProgressEventArgs : EventArgs
{
    public string FileName { get; set; }
    public double Progress { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
}
```

**Key Implementation Details**:
- Whisper models: Downloaded from HuggingFace (main -> AA)
- LLama models: Downloaded from HuggingFace
- Progress reported via IProgress<double> (0.0 to 1.0)
- Saves to appropriate model directories

---

## Implementation Order for Coding Agent

1. **Create Interfaces Folder**: Create `Interfaces/` folder in project root

2. **Create Interface Files** (in order of dependency):
   - `ISettingsService.cs` - No dependencies
   - `IThemeService.cs` - No dependencies  
   - `IAudioService.cs` - No dependencies
   - `IWhisperService.cs` - Depends on ISettingsService
   - `ILlamaService.cs` - No dependencies
   - `IDatabaseService.cs` - No dependencies
   - `IHotkeyService.cs` - Depends on ISettingsService (for saving settings)
   - `IClipboardService.cs` - No dependencies
   - `IModelDownloadService.cs` - No dependencies

3. **Modify App.xaml.cs**:
   - Add `public static IServiceProvider ServiceProvider { get; private set; }`
   - Create `ConfigureServices()` method with DI registrations
   - Replace static properties with extension methods

4. **Update Service Classes** to implement interfaces

5. **Update Consumers** to use constructor injection

---

## Extension Methods for Backward Compatibility

To maintain backward compatibility during transition, add extension methods in App.xaml.cs:

```csharp
public static class ServiceExtensions
{
    public static ISettingsService Settings(this IServiceProvider sp) 
        => sp.GetRequiredService<ISettingsService>();
    
    public static IThemeService Theme(this IServiceProvider sp) 
        => sp.GetRequiredService<IThemeService>();
    
    // ... etc for all services
}
```

Then in code that still uses `App.Settings`, replace with `App.ServiceProvider.Settings()` or update to use constructor injection.
