namespace whisperMeOff.Services;

/// <summary>
/// Audio recording constants
/// </summary>
public static class AudioConstants
{
    public const int SampleRate = 16000;
    public const int BitsPerSample = 16;
    public const int Channels = 1;
    public const int WaveFormatTag = 1; // PCM
}

/// <summary>
/// Llama model constants
/// </summary>
public static class LlamaConstants
{
    public const int DefaultContextSize = 2048;
    public const int DefaultGpuLayerCount = 35;
    public const int MaxTokens = 256;
    public const int TranslationMaxTokens = 512;
    public const float DefaultTemperature = 0.0f;
    public const float TranslationTemperature = 0.1f;
    public const float RepeatPenalty = 1.0f;
    public const int MinModelSizeMb = 100;
    public const int MaxOutputMultiplier = 3;
    public const int TranslationOutputMultiplier = 4;
}

/// <summary>
/// Application timing constants
/// </summary>
public static class TimingConstants
{
    public const int ClipboardRestoreDelayMs = 50;
    public const int ClipboardRestoreDelayLongMs = 1000;
    public const int RecordingStopTimeoutSeconds = 5;
    public const int ModelDownloadTimeoutMinutes = 30;
}

/// <summary>
/// Model file constants
/// </summary>
public static class ModelConstants
{
    public const string WhisperDefaultModel = "ggml-small.bin";
    public const string WhisperDefaultSize = "medium";
    public const int MinModelFileSizeBytes = 1024 * 1024; // 1 MB
    public const int BufferSize = 8192;
}
