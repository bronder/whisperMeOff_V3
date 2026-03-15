using System.Collections.Generic;
using whisperMeOff.Models.Transformation;

namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for text transformation including tone, grammar, voice, and style modifications.
/// Provides multiple transformation types with quality validation and user profile support.
/// </summary>
public interface ITextTransformationService
{
    /// <summary>
    /// Transforms text based on the specified transformation request.
    /// </summary>
    /// <param name="request">The transformation request containing text and parameters.</param>
    /// <returns>Transformation result with transformed text and quality metrics.</returns>
    Task<TransformationResult> TransformAsync(TransformationRequest request);

    /// <summary>
    /// Applies a transformation using a saved user profile.
    /// </summary>
    /// <param name="text">Text to transform.</param>
    /// <param name="profileId">ID of the transformation profile to use.</param>
    /// <returns>Transformation result with transformed text.</returns>
    Task<TransformationResult> TransformWithProfileAsync(string text, string profileId);

    /// <summary>
    /// Gets all available transformation profiles for the current user.
    /// </summary>
    /// <returns>List of transformation profiles.</returns>
    Task<IEnumerable<TransformationProfile>> GetProfilesAsync();

    /// <summary>
    /// Creates a new transformation profile.
    /// </summary>
    /// <param name="profile">The profile to create.</param>
    /// <returns>The created profile with assigned ID.</returns>
    Task<TransformationProfile> CreateProfileAsync(TransformationProfile profile);

    /// <summary>
    /// Updates an existing transformation profile.
    /// </summary>
    /// <param name="profile">The profile to update.</param>
    /// <returns>True if update was successful.</returns>
    Task<bool> UpdateProfileAsync(TransformationProfile profile);

    /// <summary>
    /// Deletes a transformation profile.
    /// </summary>
    /// <param name="profileId">ID of the profile to delete.</param>
    /// <returns>True if deletion was successful.</returns>
    Task<bool> DeleteProfileAsync(string profileId);

    /// <summary>
    /// Gets transformation history for analysis.
    /// </summary>
    /// <param name="limit">Maximum number of records to return.</param>
    /// <returns>List of transformation history records.</returns>
    Task<IEnumerable<TransformationHistory>> GetHistoryAsync(int limit = 50);

    /// <summary>
    /// Clears transformation history.
    /// </summary>
    /// <param name="olderThanDays">Only clear records older than specified days (0 = all).</param>
    /// <returns>Number of records cleared.</returns>
    Task<int> ClearHistoryAsync(int olderThanDays = 0);

    /// <summary>
    /// Gets available transformation types supported by the service.
    /// </summary>
    /// <returns>List of supported transformation types.</returns>
    IEnumerable<TransformationType> GetSupportedTransformations();

    /// <summary>
    /// Validates that transformation settings are properly configured.
    /// </summary>
    /// <returns>Validation result with any errors or warnings.</returns>
    Task<ValidationResult> ValidateConfigurationAsync();

    /// <summary>
    /// Estimates the quality score of a transformation without performing the full transformation.
    /// Uses lightweight analysis to provide quick feedback.
    /// </summary>
    /// <param name="originalText">Original text.</param>
    /// <param name="transformedText">Transformed text to evaluate.</param>
    /// <param name="transformationType">Type of transformation applied.</param>
    /// <returns>Quality metrics including similarity score and confidence.</returns>
    Task<QualityMetrics> EstimateQualityAsync(string originalText, string transformedText, TransformationType transformationType);
}
