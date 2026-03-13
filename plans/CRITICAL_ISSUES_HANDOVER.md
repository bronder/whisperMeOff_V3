# Critical Issues Fix - Handover Document

## Overview
This document provides detailed information for a coding agent to fix the two critical issues identified in CODE_REVIEW.md.

---

## Critical Issue #1: Static Service Locator Anti-Pattern

### Problem
The application uses 9 static properties in [`App.xaml.cs`](App.xaml.cs) that act as a Service Locator anti-pattern:
- `App.Settings` (line ~48)
- `App.Whisper` (line ~52)
- `App.Audio` (line ~56)
- `App.Llama` (line ~60)
- `App.Database` (line ~64)
- `App.Theme` (line ~68)
- `App.Clipboard` (line ~72)
- `App.Hotkey` (line ~76)
- `App.ModelDownload` (line ~80)

This makes the code impossible to unit test and creates tight coupling.

### Solution
Implement proper Dependency Injection using `Microsoft.Extensions.DependencyInjection` (v8.0.0 already added to project).

### Interfaces Created
All interfaces are in the `Interfaces/` folder:
- [`Interfaces/ISettingsService.cs`](Interfaces/ISettingsService.cs)
- [`Interfaces/IThemeService.cs`](Interfaces/IThemeService.cs)
- [`Interfaces/IAudioService.cs`](Interfaces/IAudioService.cs)
- [`Interfaces/IWhisperService.cs`](Interfaces/IWhisperService.cs)
- [`Interfaces/ILlamaService.cs`](Interfaces/ILlamaService.cs)
- [`Interfaces/IDatabaseService.cs`](Interfaces/IDatabaseService.cs)
- [`Interfaces/IHotkeyService.cs`](Interfaces/IHotkeyService.cs)
- [`Interfaces/IClipboardService.cs`](Interfaces/IClipboardService.cs)
- [`Interfaces/IModelDownloadService.cs`](Interfaces/IModelDownloadService.cs)

### Implementation Steps

#### Step 1: Modify App.xaml.cs
Add the following to App.xaml.cs:

```csharp
using Microsoft.Extensions.DependencyInjection;

// Add at class level
public static IServiceProvider Services { get; private set; } = null!;

// Add ConfigureServices method
private static void ConfigureServices(IServiceCollection services)
{
    // Register services as singletons
    services.AddSingleton<ISettingsService, SettingsService>();
    services.AddSingleton<IThemeService, ThemeService>();
    services.AddSingleton<IAudioService, AudioService>();
    services.AddSingleton<IWhisperService, WhisperService>();
    services.AddSingleton<ILlamaService, LlamaService>();
    services.AddSingleton<IDatabaseService, DatabaseService>();
    services.AddSingleton<IClipboardService, ClipboardService>();
    services.AddSingleton<IHotkeyService, HotkeyService>();
    services.AddSingleton<IModelDownloadService, ModelDownloadService>();
}
```

#### Step 2: Update OnStartup in App.xaml.cs
Replace:
```csharp
// Old - static initialization
Settings = new SettingsService();
```

With DI initialization:
```csharp
var services = new ServiceCollection();
ConfigureServices(services);
Services = services.BuildServiceProvider();

// Initialize services that need startup
await Services.GetRequiredService<ISettingsService>().LoadAsync();
await Services.GetRequiredService<IDatabaseService>().InitializeAsync();
Services.GetRequiredService<IThemeService>().Initialize();
```

#### Step 3: Update Service Classes
Make each service implement its interface:

```csharp
// Example: SettingsService.cs
public class SettingsService : ISettingsService { ... }
// Example: AudioService.cs  
public class AudioService : IAudioService { ... }
```

#### Step 4: Update ViewModels and Views
Replace static property access:
```csharp
// OLD
var text = App.Clipboard.GetText();
App.Settings.Save();

// NEW - via constructor injection
public class MainViewModel
{
    private readonly IClipboardService _clipboard;
    private readonly ISettingsService _settings;
    
    public MainViewModel(IClipboardService clipboard, ISettingsService settings)
    {
        _clipboard = clipboard;
        _settings = settings;
    }
}
```

For MainWindow.xaml.cs, use service locator pattern initially then migrate to constructor injection:
```csharp
// Temporary - can use IServiceProvider
var clipboard = App.Services.GetRequiredService<IClipboardService>();
```

---

## Critical Issue #2: Unsafe Reflection in WhisperService

### Problem
In [`Services/WhisperService.cs`](Services/WhisperService.cs) lines 152-189, there's unsafe reflection code accessing internal Whisper.NET members:

```csharp
// LINES ~152-189 - UNSAFE REFLECTION
var whisperType = whisper.GetType();
var field = whisperType.GetField("params", BindingFlags.NonPublic | BindingFlags.Instance);
var method = whisperType.GetMethod("GetParams", BindingFlags.NonPublic | BindingFlags.Instance);
// ... uses unsafe pointer operations
```

This is fragile and breaks between Whisper.NET versions.

### Solution
Use the public API instead. The `WhisperFactory` and related classes have proper public methods for configuration.

### Implementation Steps

#### Step 1: Replace the unsafe reflection section
Find the section around lines 152-189 in WhisperService.cs and replace with:

```csharp
// Instead of reflection, use the public API:
// Get parameters through the factory or builder pattern

var builder = WhisperFactoryBuilder.Create()
    .WithLanguage(selectedLanguage)
    .WithTranslate(translate)
    .WithContext(context)
    .WithSpeed(1.0f);

if (!string.IsNullOrEmpty(initialPrompt))
{
    builder.WithInitialPrompt(initialPrompt);
}

using var whisper = await builder.BuildAsync(modelPath);
```

#### Step 2: If public API is insufficient
Check if there's a `GetParams()` or similar public method in newer Whisper.NET versions. If not available, the translation feature may need to be reimplemented differently (e.g., using LLama directly for translation instead of Whisper's built-in translate).

---

## Testing Checklist

After implementing both fixes:

1. **Build Success**: Project compiles without errors
2. **Runtime Test**: Application starts and records/transcribes audio
3. **Theme Test**: Switch between themes (Dark, Light, Dracula, etc.)
4. **Hotkey Test**: Ctrl+Shift+R triggers recording
5. **Database Test**: Transcriptions are saved and retrievable
6. **Model Download**: Can download new Whisper/LLama models

---

## File Reference

### Key Files to Modify
1. `App.xaml.cs` - Add IServiceProvider, ConfigureServices, update OnStartup
2. `Services/SettingsService.cs` - Add `: ISettingsService`
3. `Services/ThemeService.cs` - Add `: IThemeService`
4. `Services/AudioService.cs` - Add `: IAudioService`
5. `Services/WhisperService.cs` - Add `: IWhisperService`, fix unsafe reflection
6. `Services/LlamaService.cs` - Add `: ILlamaService`
7. `Services/DatabaseService.cs` - Add `: IDatabaseService`
8. `Services/HotkeyService.cs` - Add `: IHotkeyService`
9. `Services/ClipboardService.cs` - Add `: IClipboardService`
10. `Services/ModelDownloadService.cs` - Add `: IModelDownloadService`
11. `ViewModels/MainViewModel.cs` - Add constructor injection (if exists)
12. `Views/MainWindow.xaml.cs` - Update to use DI or IServiceProvider

### Files Already Created (DO NOT RECREATE)
- All 9 interface files in `Interfaces/` folder
- `plans/critical_issues_fix_spec.md` - Technical specification
- `plans/service_interfaces.md` - Interface documentation
