namespace whisperMeOff.Services;

/// <summary>
/// Application-wide constants for consistent configuration values.
/// </summary>
public static class AppConstants
{
    // Audio settings
    public const int PreBufferMs = 300;
    public const int SampleRate = 16000;
    public const int BitsPerSample = 16;
    public const int Channels = 1;

    // Hotkey settings
    public const int DefaultHotkeyId = 9000;
    public const uint HotkeyModifiers = 0x0002 | 0x0004; // CTRL + SHIFT

    // UI settings
    public const double DefaultTemperature = 0.7;
    public const int DefaultMaxTokens = 4096;
    public const int DefaultQualityThreshold = 70;
    public const int DefaultMaxTextLength = 10000;
    public const int DefaultHistoryRetentionDays = 30;

    // Timeouts
    public const int RecordingStopTimeoutMs = 5000;
    public const int ClipboardRestoreDelayMs = 1000;
    public const int WindowReadyTimeoutMs = 500;

    // Model settings
    public const long MinModelSizeBytes = 100 * 1024 * 1024; // 100 MB
    public const long MaxFileSizeBytes = 500 * 1024 * 1024; // 500 MB
    public const int DefaultContextSize = 2048;
    public const int DefaultGpuLayers = 35;

    // Logging
    public const int MaxLogArchiveDays = 7;
}
