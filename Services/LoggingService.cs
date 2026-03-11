using NLog;
using System.IO;

namespace whisperMeOff.Services;

/// <summary>
/// Centralized logging service using NLog
/// </summary>
public static class LoggingService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    public static void Initialize()
    {
        // Ensure log directory exists
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "whisperMeOff", "logs");
        
        Directory.CreateDirectory(logDir);
        
        // Configure NLog programmatically
        var config = new NLog.Config.LoggingConfiguration();
        
        // Main log file - all levels
        var fileTarget = new NLog.Targets.FileTarget("logfile")
        {
            FileName = Path.Combine(logDir, "whisperMeOff-${shortdate}.log"),
            Layout = "${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}",
            ArchiveEvery = NLog.Targets.FileArchivePeriod.Day,
            MaxArchiveFiles = 7,
            ConcurrentWrites = true,
            KeepFileOpen = false
        };
        
        // Error log file - errors only (filtered by rule)
        var errorTarget = new NLog.Targets.FileTarget("errorfile")
        {
            FileName = Path.Combine(logDir, "errors-${shortdate}.log"),
            Layout = "${longdate} [${level:uppercase=true}] ${logger}${newline}${exception:format=tostring}${newline}"
        };
        
        config.AddTarget(fileTarget);
        config.AddTarget(errorTarget);
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);
        config.AddRule(NLog.LogLevel.Error, NLog.LogLevel.Fatal, errorTarget);
        
        LogManager.Configuration = config;
        
        Info("Logging initialized - whisperMeOff starting");
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
