# Critical Issues Fix Specification

This document provides detailed technical specifications for fixing the two remaining critical issues identified in `CODE_REVIEW.md`.

---

## Critical Issue #1: Static Service Locator Anti-Pattern

### Location
- **File:** `App.xaml.cs` lines 24-32
- **Current Code:**
```csharp
public static SettingsService Settings { get; private set; } = null!;
public static ThemeService Theme { get; private set; } = null!;
public static AudioService Audio { get; private set; } = null!;
public static WhisperService Whisper { get; private set; } = null!;
public static LlamaService Llama { get; private set; } = null!;
public static DatabaseService Database { get; private set; } = null!;
public static HotkeyService Hotkey { get; private set; } = null!;
public static ClipboardService Clipboard { get; private set; } = null!;
public static ModelDownloadService ModelDownload { get; private set; } = null!;
```

### Why This Is Bad
1. Creates hidden global dependencies throughout the codebase
2. Makes unit testing impossible without mocking the entire App class
3. Tight coupling between all components
4. No way to swap implementations (e.g., for testing)

### Solution: Implement Dependency Injection

#### Step 1: Add Microsoft.Extensions.DependencyInjection Package
Add to `whisperMeOff.csproj`:
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
```

#### Step 2: Create Service Interfaces

Create `Services/ISettingsService.cs`:
```csharp
namespace whisperMeOff.Services;

public interface ISettingsService
{
    SettingsService.WhisperSettings Whisper { get; }
    SettingsService.GeneralSettings General { get; }
    SettingsService.LlamaSettings Llama { get; }
    Task LoadAsync();
    Task SaveAsync();
}
```

Create `Services/IAudioService.cs`:
```csharp
namespace whisperMeOff.Services;

public interface IAudioService
{
    bool IsRecording { get; }
    event EventHandler<byte[]>? DataAvailable;
    Task StartRecordingAsync();
    Task StopRecordingAsync();
    void Dispose();
}
```

Create other interfaces similarly: `IWhisperService`, `ILlamaService`, `IDatabaseService`, `IHotkeyService`, `IClipboardService`, `IThemeService`, `IModelDownloadService`

#### Step 3: Modify App.xaml.cs

Add DI container field:
```csharp
public static IServiceProvider ServiceProvider { get; private set; } = null!;
```

Replace static service properties with extension method:
```csharp
public static class AppExtensions
{
    public static ISettingsService Settings => App.ServiceProvider.GetRequiredService<ISettingsService>();
    public static IAudioService Audio => App.ServiceProvider.GetRequiredService<IAudioService>();
    public static IWhisperService Whisper => App.ServiceProvider.GetRequiredService<IWhisperService>();
    // etc.
}
```

Update `OnStartup` method to register services:
```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    // ... existing path initialization code ...

    // Configure services
    var services = new ServiceCollection();
    ConfigureServices(services);
    ServiceProvider = services.BuildServiceProvider();

    // Initialize services
    await InitializeServicesAsync();
}

private void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<ISettingsService, SettingsService>();
    services.AddSingleton<IAudioService, AudioService>();
    services.AddSingleton<IWhisperService, WhisperService>();
    services.AddSingleton<ILlamaService, LlamaService>();
    services.AddSingleton<IDatabaseService, DatabaseService>();
    services.AddSingleton<IHotkeyService, HotkeyService>();
    services.AddSingleton<IClipboardService, ClipboardService>();
    services.AddSingleton<IThemeService, ThemeService>();
    services.AddSingleton<IModelDownloadService, ModelDownloadService>();
}

private async Task InitializeServicesAsync()
{
    var settings = ServiceProvider.GetRequiredService<ISettingsService>();
    await settings.LoadAsync();
    
    var database = ServiceProvider.GetRequiredService<IDatabaseService>();
    await database.InitializeAsync();
    
    // Initialize other services as needed
}
```

#### Step 4: Update Service References

Replace all `App.Settings` with `App.Current.Settings` (or the extension method)
Replace all `App.Audio` with `App.Current.Audio`, etc.

#### Step 5: Update Views

Views that need services should receive them through constructor injection:
```csharp
public MainWindow(IAudioService audioService, ISettingsService settingsService)
{
    _audioService = audioService;
    _settingsService = settingsService;
}
```

Register in App.xaml.cs:
```csharp
services.AddTransient<MainWindow>();
```

---

## Critical Issue #3: Unsafe Reflection for Whisper Translation

### Location
- **File:** `Services/WhisperService.cs` lines 152-189
- **Current Code:**
```csharp
// Try to enable translation if requested - use reflection to find the method
if (App.Settings.Whisper.Translate)
{
    try
    {
        // Try to find and invoke WithTask method
        var builderType = builder.GetType();
        var withTaskMethod = builderType.GetMethod("WithTask");
        if (withTaskMethod != null)
        {
            // Look for WhisperTask enum
            var whisperTaskType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "WhisperTask");
            
            if (whisperTaskType != null)
            {
                var translateValue = Enum.GetValues(whisperTaskType)
                    .Cast<object>()
                    .FirstOrDefault(v => v.ToString() == "Translate");
                
                if (translateValue != null)
                {
                    withTaskMethod.Invoke(builder, new[] { translateValue });
                    LoggingService.Info("[Whisper] Translation enabled via WithTask");
                }
            }
        }
        // ... more fallback code
    }
    catch (Exception ex)
    {
        LoggingService.Warn($"[Whisper] Translation setup failed: {ex.Message}");
    }
}
```

### Why This Is Bad
1. Fragile - breaks silently when API changes
2. No compile-time type checking
3. Hard to debug when it fails
4. Uses `dynamic` which loses type safety

### Solution Options

#### Option A: Check for Stable Public API (Recommended)

Research Whisper.net 1.9.1-preview1 API for stable translation support. The package may have public methods that can be used directly. Check:

1. The `WhisperFactory` builder API documentation
2. Look for translation-specific options in the builder pattern
3. Check if there's a `WithTask` or similar method that doesn't require reflection

If a stable API exists, replace the reflection code with:
```csharp
if (App.Settings.Whisper.Translate)
{
    // Use stable public API if available
    // Example (verify with actual API):
    builder = builder.WithTask(WhisperTask.Translate);
}
```

#### Option B: Create Version Abstraction

If the API changes frequently, create an abstraction layer:

```csharp
public interface IWhisperOptionsBuilder
{
    IWhisperOptionsBuilder WithLanguage(string language);
    IWhisperOptionsBuilder WithTranslation(bool enable);
    IWhisperOptionsBuilder WithInitialPrompt(string prompt);
    object Build();
}

public class WhisperOptionsBuilder : IWhisperOptionsBuilder
{
    private readonly IWhisperBuilder _builder;
    
    public WhisperOptionsBuilder(IWhisperBuilder builder)
    {
        _builder = builder;
    }
    
    public IWhisperOptionsBuilder WithLanguage(string language)
    {
        _builder.WithLanguage(language);
        return this;
    }
    
    public IWhisperOptionsBuilder WithTranslation(bool enable)
    {
        if (enable)
        {
            EnableTranslation();
        }
        return this;
    }
    
    public IWhisperOptionsBuilder WithInitialPrompt(string prompt)
    {
        // Apply initial prompt
        return this;
    }
    
    public object Build() => _builder.Build();
    
    private void EnableTranslation()
    {
        // Version-safe implementation
        try
        {
            // Try known stable approaches for this version
            // Option 1: Check for specific Whisper.net version
            // Option 2: Use conditional compilation
            // Option 3: Log warning and skip if not available
        }
        catch
        {
            LoggingService.Warn("Translation not available in this Whisper.net version");
        }
    }
}
```

#### Option C: Remove Translation Feature (Simplest)

If translation is not critical, simplify by removing the feature:

```csharp
// In TranscribeAsync method, remove the translation block entirely
// Add a comment explaining why:

// Translation feature removed due to unstable API in Whisper.net 1.9.1-preview1
// The reflection-based approach is fragile and breaks silently
// Re-add when stable public API becomes available
```

### Recommended Approach

**Option A** is recommended - investigate if Whisper.net 1.9.1 has stable translation API that doesn't require reflection. Check:
- Whisper.net NuGet package documentation
- GitHub repository issues/discussions
- Source code for the builder pattern

If no stable API exists, **Option C** provides the cleanest solution - remove the fragile reflection code and log a clear explanation.

---

## Implementation Order

1. **First:** Fix Critical Issue #1 (Static Service Locator)
   - This is a larger change that affects the entire codebase
   - Provides better architecture for future changes

2. **Second:** Fix Critical Issue #3 (Unsafe Reflection)
   - Can be done independently
   - Simpler to implement

---

## Testing Checklist

After implementing fixes:

- [ ] Application compiles without errors
- [ ] Application starts successfully
- [ ] Recording and transcription still works
- [ ] Settings are persisted correctly
- [ ] Theme switching works
- [ ] All services initialize properly

---

## Files to Modify

1. `whisperMeOff.csproj` - Add DI package
2. `App.xaml.cs` - Add DI container, remove static services
3. `Services/*.cs` - Add interface files, update to implement interfaces
4. `Views/MainWindow.xaml.cs` - Update to use injected services
5. Any other files using `App.Settings`, `App.Audio`, etc.
