namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for audio recording from microphone
/// </summary>
public interface IAudioService : IDisposable
{
    /// <summary>
    /// Whether currently recording
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Duration of last recording in seconds
    /// </summary>
    double LastRecordingDuration { get; }

    /// <summary>
    /// Raised when recording starts
    /// </summary>
    event EventHandler? RecordingStarted;

    /// <summary>
    /// Raised when recording stops
    /// </summary>
    event EventHandler? RecordingStopped;

    /// <summary>
    /// Raised when audio level changes (0.0 to 1.0)
    /// </summary>
    event EventHandler<double>? AudioLevelChanged;

    /// <summary>
    /// Get list of available audio input devices
    /// </summary>
    List<AudioDeviceInfo> GetAvailableDevices();

    /// <summary>
    /// Start recording from microphone
    /// </summary>
    void StartRecording();

    /// <summary>
    /// Stop recording and return the audio file path
    /// </summary>
    Task<string?> StopRecordingAsync();
}

/// <summary>
/// Information about an audio device
/// </summary>
public class AudioDeviceInfo
{
    /// <summary>
    /// Device identifier
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Number of channels
    /// </summary>
    public int Channels { get; set; }

    /// <summary>
    /// Sample rate in Hz
    /// </summary>
    public int SampleRate { get; set; }
}
