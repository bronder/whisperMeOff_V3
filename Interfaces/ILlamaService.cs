namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for text formatting and translation using LLama model
/// </summary>
public interface ILlamaService : IDisposable
{
    /// <summary>
    /// Whether the model is loaded
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Path to the current model file
    /// </summary>
    string? ModelPath { get; }

    /// <summary>
    /// Raised when model load state changes
    /// </summary>
    event EventHandler<bool>? ModelLoaded;

    /// <summary>
    /// Initialize the LLama model
    /// </summary>
    /// <param name="modelPath">Path to GGUF model file</param>
    Task InitializeAsync(string modelPath);

    /// <summary>
    /// Format raw transcription text (correct grammar/punctuation)
    /// </summary>
    /// <param name="rawText">Raw transcription text</param>
    /// <returns>Formatted text</returns>
    Task<string> FormatTextAsync(string rawText);

    /// <summary>
    /// Translate text to target language
    /// </summary>
    /// <param name="rawText">Text to translate</param>
    /// <param name="targetLanguage">Target language code (default: "en")</param>
    /// <returns>Translated text</returns>
    Task<string> TranslateTextAsync(string rawText, string targetLanguage = "en");
}
