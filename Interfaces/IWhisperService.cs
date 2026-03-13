namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for speech-to-text transcription using Whisper
/// </summary>
public interface IWhisperService : IDisposable
{
    /// <summary>
    /// Whether the Whisper model is loaded
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initialize the Whisper model
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Get the path to the current model
    /// </summary>
    string GetModelPath();

    /// <summary>
    /// Transcribe an audio file
    /// </summary>
    /// <param name="audioPath">Path to audio file (WAV format)</param>
    /// <returns>Transcribed text</returns>
    Task<string> TranscribeAsync(string audioPath);

    /// <summary>
    /// Reload the model with a different path
    /// </summary>
    /// <param name="modelPath">Path to model file</param>
    void ReloadModel(string modelPath);
}
