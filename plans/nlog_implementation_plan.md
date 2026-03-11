# NLog Implementation Plan for whisperMeOff

## Overview
Add NLog logging to whisperMeOff for persistent, structured error tracking and debugging.

---

## Dependencies Needed

Add to `whisperMeOff.csproj`:
```xml
<PackageReference Include="NLog" Version="5.2.8" />
<PackageReference Include="NLog.Extensions.Logging" Version="5.3.8" />
```

Or via dotnet CLI:
```bash
dotnet add package NLog
dotnet add package NLog.Extensions.Logging
```

---

## Step 1: Create NLog Configuration

### Option A: NLog.config file (in project root)

Create `NLog.config`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
      autoReload="true"
      throwConfigExceptions="true">

  <variable name="logDirectory" value="${specialfolder:folder=LocalApplicationData}/whisperMeOff/logs" />

  <targets>
    <!-- Main log file -->
    <target xsi:type="File" 
            name="logfile" 
            fileName="${logDirectory}/whisperMeOff-${shortdate}.log"
            layout="${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}"
            archiveFileName="${logDirectory}/archives/whisperMeOff-{#}.log"
            archiveEvery="Day"
            archiveNumbering="Date"
            maxArchiveFiles="7"
            concurrentWrites="true"
            keepFileOpen="false" />

    <!-- Error-only log file -->
    <target xsi:type="File"
            name="errorfile"
            fileName="${logDirectory}/errors-${shortdate}.log"
            layout="${longdate} [${level:uppercase=true}] ${logger}${newline}${exception:format=tostring}${newline}"
            threshold="Error" />

    <!-- Debug output -->
    <target xsi:type="Debugger"
            name="debugger"
            layout="${time} [${level:uppercase=true}] ${message}" />
  </targets>

  <rules>
    <logger name="*" minlevel="Debug" writeTo="logfile" />
    <logger name="*" minlevel="Error" writeTo="errorfile" />
    <logger name="*" minlevel="Debug" writeTo="debugger" />
  </rules>
</nlog>
```

**Important:** Set Build Action to "Content" and "Copy to Output Directory" to "Preserve Newest" in file properties.

### Option B: Code-based configuration (simpler)

Skip NLog.config and configure in code (less flexible but no extra files).

---

## Step 2: Create Logging Service

Create `Services/LoggingService.cs`:

```csharp
using NLog;

namespace whisperMeOff.Services;

public static class LoggingService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    public static void Initialize()
    {
        // Configure NLog
        var config = new NLog.Config.LoggingConfiguration();
        
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "whisperMeOff", "logs");
        
        // Main log file
        var fileTarget = new NLog.Targets.FileTarget("logfile")
        {
            FileName = Path.Combine(logDir, "whisperMeOff-${shortdate}.log"),
            Layout = "${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}",
            ArchiveEvery = NLog.Targets.FileArchivePeriod.Day,
            MaxArchiveFiles = 7,
            ConcurrentWrites = true
        };
        
        // Error log file
        var errorTarget = new NLog.Targets.FileTarget("errorfile")
        {
            FileName = Path.Combine(logDir, "errors-${shortdate}.log"),
            Layout = "${longdate} [${level:uppercase=true}] ${logger}${newline}${exception:format=tostring}${newline}",
            Threshold = NLog.LogLevel.Error
        };
        
        config.AddTarget(fileTarget);
        config.AddTarget(errorTarget);
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);
        config.AddRule(NLog.LogLevel.Error, NLog.LogLevel.Fatal, errorTarget);
        
        LogManager.Configuration = config;
        
        Info("Logging initialized");
    }
    
    public static void Trace(string message) => Logger.Trace(message);
    public static void Debug(string message) => Logger.Debug(message);
    public static void Info(string message) => Logger.Info(message);
    public static void Warn(string message) => Logger.Warn(message);
    public static void Error(string message) => Logger.Error(message);
    public static void Error(Exception ex, string message) => Logger.Error(ex, message);
    public static void Fatal(Exception ex, string message) => Logger.Fatal(ex, message);
    
    public static void Shutdown()
    {
        Info("Application shutting down");
        LogManager.Shutdown();
    }
}
```

---

## Step 3: Update App.xaml.cs

In `App.xaml.cs`, initialize logging at startup:

```csharp
public App()
{
    // Initialize logging first
    LoggingService.Initialize();
    
    // Set up global exception handlers
    SetupExceptionHandling();
}

private void SetupExceptionHandling()
{
    // Handle exceptions on UI thread
    DispatcherUnhandledException += (s, e) =>
    {
        LoggingService.Fatal(e.Exception, "Unhandled UI exception");
        MessageBox.Show(
            $"An unexpected error occurred:\n{e.Exception.Message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    };
    
    // Handle exceptions from background threads
    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
    {
        if (e.ExceptionObject is Exception ex)
        {
            LoggingService.Fatal(ex, "Unhandled domain exception");
        }
    };
    
    // Handle unobserved task exceptions
    TaskScheduler.UnobservedTaskException += (s, e) =>
    {
        LoggingService.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    };
}
```

---

## Step 4: Update Services to Use Logging

### Example: WhisperService.cs

Replace all `System.Diagnostics.Debug.WriteLine` with logging calls:

```csharp
// Before
System.Diagnostics.Debug.WriteLine($"[Whisper] Transcription complete: {transcription}");

// After
LoggingService.Debug($"Transcription complete: {transcription}");
```

```csharp
// Before
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Transcription error: {ex.Message}");
    throw;
}

// After
catch (Exception ex)
{
    LoggingService.Error(ex, "Transcription failed");
    throw;
}
```

---

## Step 5: Files to Update

Replace all `System.Diagnostics.Debug.WriteLine` with `LoggingService.*` calls:

| File | Change |
|------|--------|
| `Services/WhisperService.cs` | Replace Debug.WriteLine |
| `Services/LlamaService.cs` | Replace Debug.WriteLine |
| `Services/AudioService.cs` | Replace Debug.WriteLine |
| `Services/SettingsService.cs` | Replace Debug.WriteLine |
| `Services/ModelDownloadService.cs` | Replace Debug.WriteLine |
| `Services/ClipboardService.cs` | Replace Debug.WriteLine |
| `Services/DatabaseService.cs` | Replace Debug.WriteLine |
| `Services/HotkeyService.cs` | Replace Debug.WriteLine |
| `Services/ThemeService.cs` | Replace Debug.WriteLine |
| `Views/MainWindow.xaml.cs` | Replace Debug.WriteLine |
| `App.xaml.cs` | Add initialization + exception handlers |

---

## Step 6: Verify

1. Run the app
2. Check `%LOCALAPPDATA%\whisperMeOff\logs\` for log files
3. Verify logs are being created with timestamps

---

## Log File Locations

| Log Type | Location |
|----------|----------|
| All logs | `%LOCALAPPDATA%\whisperMeOff\logs\whisperMeOff-YYYY-MM-DD.log` |
| Errors only | `%LOCALAPPDATA%\whisperMeOff\logs\errors-YYYY-MM-DD.log` |

---

## Benefits

1. **Persistent logs** - Survive app restarts
2. **Daily rotation** - Automatic file management  
3. **Error separation** - Easy to find issues
4. **Structured format** - Timestamp, level, source, message
5. **Archive** - Keep 7 days of history
6. **Production-ready** - Industry standard
