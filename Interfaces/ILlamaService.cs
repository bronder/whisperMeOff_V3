using whisperMeOff.Models.Transformation;

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
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Formatted text</returns>
    Task<string> FormatTextAsync(string rawText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Translate text to target language
    /// </summary>
    /// <param name="rawText">Text to translate</param>
    /// <param name="targetLanguage">Target language code (default: "en")</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Translated text</returns>
    Task<string> TranslateTextAsync(string rawText, string targetLanguage = "en", CancellationToken cancellationToken = default);

    /// <summary>
    /// Transform text based on specified transformation type and direction
    /// </summary>
    /// <param name="request">Transformation request containing text, type, and direction</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Transformation result with transformed text and metadata</returns>
    Task<TransformationResult> TransformTextAsync(TransformationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transform text with custom profile settings
    /// </summary>
    /// <param name="text">Text to transform</param>
    /// <param name="profile">Custom transformation profile</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Transformation result</returns>
    Task<TransformationResult> TransformWithProfileAsync(string text, TransformationProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply multiple transformations in sequence
    /// </summary>
    /// <param name="text">Text to transform</param>
    /// <param name="transformations">List of transformations to apply in order</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Final transformation result</returns>
    Task<TransformationResult> TransformBatchAsync(string text, List<TransformationRequest> transformations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get available transformation types supported by the model
    /// </summary>
    /// <returns>List of supported transformation types</returns>
    List<TransformationType> GetSupportedTransformations();

    /// <summary>
    /// Estimate token count for transformation prompt
    /// </summary>
    /// <param name="text">Text to estimate</param>
    /// <returns>Estimated token count</returns>
    int EstimateTokens(string text);
}
