using System;
using System.Collections.Generic;

namespace whisperMeOff.Models.Transformation;

/// <summary>
/// Defines the types of text transformations supported by the transformation service.
/// </summary>
public enum TransformationType
{
    /// <summary>
    /// Convert between formal and informal language
    /// </summary>
    Tone,

    /// <summary>
    /// Convert between passive and active voice
    /// </summary>
    Voice,

    /// <summary>
    /// Simplify or elaborate text complexity
    /// </summary>
    Complexity,

    /// <summary>
    /// Adjust text between professional and casual tone
    /// </summary>
    Professionalism,

    /// <summary>
    /// Apply user-defined personal style preferences
    /// </summary>
    PersonalStyle,

    /// <summary>
    /// Correct grammar and punctuation
    /// </summary>
    Grammar,

    /// <summary>
    /// Translate to a different language
    /// </summary>
    Translation,

    /// <summary>
    /// Custom transformation defined by user
    /// </summary>
    Custom
}

/// <summary>
/// Specifies the direction or target of a transformation.
/// </summary>
public enum TransformationDirection
{
    /// <summary>
    /// Default/directional transformation
    /// </summary>
    Default,

    /// <summary>
    /// Convert to formal tone
    /// </summary>
    Formal,

    /// <summary>
    /// Convert to informal tone
    /// </summary>
    Informal,

    /// <summary>
    /// Convert to active voice
    /// </summary>
    Active,

    /// <summary>
    /// Convert to passive voice
    /// </summary>
    Passive,

    /// <summary>
    /// Simplify text (reduce complexity)
    /// </summary>
    Simplify,

    /// <summary>
    /// Elaborate text (add detail)
    /// </summary>
    Elaborate,

    /// <summary>
    /// Professional tone
    /// </summary>
    Professional,

    /// <summary>
    /// Casual tone
    /// </summary>
    Casual
}

/// <summary>
/// Request model for text transformation operations.
/// </summary>
public class TransformationRequest
{
    /// <summary>
    /// The text to transform.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The type of transformation to apply.
    /// </summary>
    public TransformationType TransformationType { get; set; } = TransformationType.Grammar;

    /// <summary>
    /// The direction or target of the transformation.
    /// </summary>
    public TransformationDirection Direction { get; set; } = TransformationDirection.Default;

    /// <summary>
    /// Optional: Target language code for translation (e.g., "en", "es", "fr").
    /// </summary>
    public string? TargetLanguage { get; set; }

    /// <summary>
    /// Optional: Custom transformation parameters for advanced users.
    /// </summary>
    public Dictionary<string, string>? CustomParameters { get; set; }

    /// <summary>
    /// Whether to preserve proper nouns (names, places, organizations).
    /// </summary>
    public bool PreserveProperNouns { get; set; } = true;

    /// <summary>
    /// Whether to preserve technical terminology.
    /// </summary>
    public bool PreserveTechnicalTerms { get; set; } = true;

    /// <summary>
    /// Minimum quality threshold (0-100) below which transformation is rejected.
    /// </summary>
    public int MinQualityThreshold { get; set; } = 70;

    /// <summary>
    /// Whether to include quality metrics in the response.
    /// </summary>
    public bool IncludeQualityMetrics { get; set; } = true;

    /// <summary>
    /// ID of user profile to use (optional).
    /// </summary>
    public string? ProfileId { get; set; }
}

/// <summary>
/// Result of a text transformation operation.
/// </summary>
public class TransformationResult
{
    /// <summary>
    /// The transformed text.
    /// </summary>
    public string TransformedText { get; set; } = string.Empty;

    /// <summary>
    /// Original text for reference.
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Type of transformation that was applied.
    /// </summary>
    public TransformationType TransformationType { get; set; }

    /// <summary>
    /// Direction of transformation that was applied.
    /// </summary>
    public TransformationDirection Direction { get; set; }

    /// <summary>
    /// Quality metrics of the transformation.
    /// </summary>
    public QualityMetrics? QualityMetrics { get; set; }

    /// <summary>
    /// Whether the transformation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if transformation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// List of terms that were preserved (proper nouns, technical terms).
    /// </summary>
    public List<string> PreservedTerms { get; set; } = new();

    /// <summary>
    /// Timestamp when transformation was performed.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Processing time in milliseconds.
    /// </summary>
    public long ProcessingTimeMs { get; set; }
}

/// <summary>
/// Quality metrics for transformation output.
/// </summary>
public class QualityMetrics
{
    /// <summary>
    /// Semantic similarity score between original and transformed (0-100).
    /// Higher means more similar meaning is preserved.
    /// </summary>
    public float SimilarityScore { get; set; }

    /// <summary>
    /// Confidence score of the transformation quality (0-100).
    /// </summary>
    public float ConfidenceScore { get; set; }

    /// <summary>
    /// Readability score of the transformed text (0-100).
    /// </summary>
    public float ReadabilityScore { get; set; }

    /// <summary>
    /// Structural preservation score (0-100).
    /// </summary>
    public float StructureScore { get; set; }

    /// <summary>
    /// Overall quality score (0-100).
    /// Calculated as weighted combination of other metrics.
    /// </summary>
    public float OverallScore { get; set; }

    /// <summary>
    /// Whether meaning fidelity is considered acceptable.
    /// </summary>
    public bool IsMeaningFidelityAcceptable => SimilarityScore >= 70;

    /// <summary>
    /// Whether the transformation quality meets the threshold.
    /// </summary>
    public bool MeetsQualityThreshold { get; set; }

    /// <summary>
    /// Detailed issues found during quality assessment.
    /// </summary>
    public List<QualityIssue> Issues { get; set; } = new();
}

/// <summary>
/// Represents a quality issue found during transformation assessment.
/// </summary>
public class QualityIssue
{
    /// <summary>
    /// Severity of the issue.
    /// </summary>
    public QualityIssueSeverity Severity { get; set; }

    /// <summary>
    /// Description of the issue.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Location in text where issue occurs (optional).
    /// </summary>
    public string? Location { get; set; }
}

/// <summary>
/// Severity levels for quality issues.
/// </summary>
public enum QualityIssueSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// User-defined transformation profile with custom settings.
/// </summary>
public class TransformationProfile
{
    /// <summary>
    /// Unique identifier for the profile.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// User-friendly name for the profile.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this profile does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Primary transformation type for this profile.
    /// </summary>
    public TransformationType TransformationType { get; set; }

    /// <summary>
    /// Direction of transformation.
    /// </summary>
    public TransformationDirection Direction { get; set; }

    /// <summary>
    /// Custom parameters specific to this profile.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>
    /// Whether to preserve proper nouns.
    /// </summary>
    public bool PreserveProperNouns { get; set; } = true;

    /// <summary>
    /// Whether to preserve technical terms.
    /// </summary>
    public bool PreserveTechnicalTerms { get; set; } = true;

    /// <summary>
    /// Minimum quality threshold for this profile.
    /// </summary>
    public int MinQualityThreshold { get; set; } = 70;

    /// <summary>
    /// Whether this is the default profile.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// When the profile was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the profile was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Usage count for this profile.
    /// </summary>
    public int UsageCount { get; set; }
}

/// <summary>
/// Database record for transformation profile (used by DatabaseService).
/// Uses long Id and stores enums as strings.
/// </summary>
public class TransformationProfileRecord
{
    /// <summary>
    /// Unique identifier for the profile (database auto-increment).
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// User-friendly name for the profile.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this profile does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Primary transformation type for this profile (stored as string).
    /// </summary>
    public string TransformationType { get; set; } = "Grammar";

    /// <summary>
    /// Direction of transformation (stored as string).
    /// </summary>
    public string Direction { get; set; } = "Default";

    /// <summary>
    /// Temperature for LLM generation (0.0-2.0).
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Maximum tokens for LLM response.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Whether to preserve proper nouns.
    /// </summary>
    public bool PreserveProperNouns { get; set; } = true;

    /// <summary>
    /// Whether to preserve technical terms.
    /// </summary>
    public bool PreserveTechnicalTerms { get; set; } = true;

    /// <summary>
    /// Custom prompt template for this profile.
    /// </summary>
    public string? CustomPrompt { get; set; }

    /// <summary>
    /// Whether this is the default profile.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// When the profile was created (ISO 8601 string).
    /// </summary>
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>
    /// When the profile was last modified (ISO 8601 string).
    /// </summary>
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

/// <summary>
/// Record of a transformation operation for history tracking.
/// </summary>
public class TransformationHistory
{
    /// <summary>
    /// Unique identifier for this history record.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Original text that was transformed.
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Transformed result text.
    /// </summary>
    public string TransformedText { get; set; } = string.Empty;

    /// <summary>
    /// Type of transformation applied.
    /// </summary>
    public TransformationType TransformationType { get; set; }

    /// <summary>
    /// Direction of transformation.
    /// </summary>
    public TransformationDirection Direction { get; set; }

    /// <summary>
    /// Profile ID used (if any).
    /// </summary>
    public string? ProfileId { get; set; }

    /// <summary>
    /// Quality score of the transformation.
    /// </summary>
    public float QualityScore { get; set; }

    /// <summary>
    /// Whether the transformation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if transformation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Processing time in milliseconds.
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// When this transformation was performed.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Database record for transformation history (used by DatabaseService).
/// Uses long Id and stores enums as strings.
/// </summary>
public class TransformationHistoryRecord
{
    /// <summary>
    /// Unique identifier for this history record (database auto-increment).
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The original text before transformation.
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// The transformed text after processing.
    /// </summary>
    public string TransformedText { get; set; } = string.Empty;

    /// <summary>
    /// Type of transformation applied (stored as string).
    /// </summary>
    public string TransformationType { get; set; } = "Grammar";

    /// <summary>
    /// Direction of transformation (stored as string).
    /// </summary>
    public string Direction { get; set; } = "Default";

    /// <summary>
    /// ID of the transformation profile used (if any).
    /// </summary>
    public long? ProfileId { get; set; }

    /// <summary>
    /// Quality score (0.0-1.0) if calculated.
    /// </summary>
    public double? QualityScore { get; set; }

    /// <summary>
    /// Time taken to process in milliseconds.
    /// </summary>
    public int ProcessingTimeMs { get; set; }

    /// <summary>
    /// When this transformation was performed (ISO 8601 string).
    /// </summary>
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>
    /// ID of the transcription this transformation is associated with (if any).
    /// </summary>
    public long? TranscriptionId { get; set; }
}

/// <summary>
/// Result of configuration validation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether validation passed.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// List of validation errors.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// List of validation warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// List of informational messages.
    /// </summary>
    public List<string> Infos { get; set; } = new();
}

/// <summary>
/// Settings for transformation behavior.
/// </summary>
public class TransformationSettings
{
    /// <summary>
    /// Whether transformation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default transformation type to use.
    /// </summary>
    public TransformationType DefaultTransformationType { get; set; } = TransformationType.Grammar;

    /// <summary>
    /// Default direction for transformations.
    /// </summary>
    public TransformationDirection DefaultDirection { get; set; } = TransformationDirection.Default;

    /// <summary>
    /// Default language for translations.
    /// </summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>
    /// Default quality threshold.
    /// </summary>
    public int DefaultQualityThreshold { get; set; } = 70;

    /// <summary>
    /// Whether to preserve proper nouns by default.
    /// </summary>
    public bool PreserveProperNouns { get; set; } = true;

    /// <summary>
    /// Whether to preserve technical terms by default.
    /// </summary>
    public bool PreserveTechnicalTerms { get; set; } = true;

    /// <summary>
    /// Maximum text length for transformation.
    /// </summary>
    public int MaxTextLength { get; set; } = 10000;

    /// <summary>
    /// Maximum transformations to keep in history.
    /// </summary>
    public int MaxHistoryCount { get; set; } = 1000;

    /// <summary>
    /// History retention days (0 = forever).
    /// </summary>
    public int HistoryRetentionDays { get; set; } = 30;

    /// <summary>
    /// ID of the default transformation profile.
    /// </summary>
    public string? DefaultProfileId { get; set; }

    /// <summary>
    /// Whether to auto-save transformation history.
    /// </summary>
    public bool AutoSaveHistory { get; set; } = true;

    /// <summary>
    /// Whether to show quality metrics in results.
    /// </summary>
    public bool ShowQualityMetrics { get; set; } = true;
}
