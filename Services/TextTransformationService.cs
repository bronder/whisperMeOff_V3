using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using whisperMeOff.Interfaces;
using whisperMeOff.Models.Transformation;

namespace whisperMeOff.Services;

/// <summary>
/// Service for transforming text with various transformation types.
/// Coordinates between the Llama model for transformation and quality scoring.
/// </summary>
public class TextTransformationService : ITextTransformationService
{
    private readonly ILlamaService _llamaService;
    
    private TransformationSettings _settings;
    private Dictionary<string, TransformationProfile> _profileCache = new();
    private List<TransformationHistory> _history = new();
    private bool _isInitialized = false;

    // Default settings
    private const int DefaultMaxTextLength = 10000;
    private const int DefaultQualityThreshold = 70;

    public TextTransformationService(ILlamaService llamaService)
    {
        _llamaService = llamaService ?? throw new ArgumentNullException(nameof(llamaService));
        _settings = new TransformationSettings();
    }

    /// <summary>
    /// Initializes the transformation service and loads settings.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            LoggingService.Info("Initializing TextTransformationService");
            
            // Load settings - use defaults for now
            _settings = new TransformationSettings();
            
            // Load cached profiles
            await LoadProfilesAsync();
            
            _isInitialized = true;
            LoggingService.Info("TextTransformationService initialized successfully");
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to initialize TextTransformationService");
            throw;
        }
    }

    /// <summary>
    /// Transforms text according to the specified request.
    /// </summary>
    public async Task<TransformationResult> TransformAsync(TransformationRequest request)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = "Text cannot be empty",
                OriginalText = request.Text
            };
        }

        if (request.Text.Length > _settings.MaxTextLength)
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = $"Text exceeds maximum length of {_settings.MaxTextLength} characters",
                OriginalText = request.Text
            };
        }

        if (!_llamaService.IsLoaded)
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = "Llama model is not loaded",
                OriginalText = request.Text
            };
        }

        var startTime = DateTime.UtcNow;
        var preservedTerms = new List<string>();

        try
        {
            // Extract terms to preserve if requested
            if (request.PreserveProperNouns || request.PreserveTechnicalTerms)
            {
                preservedTerms = ExtractTermsToPreserve(request.Text, request.PreserveProperNouns, request.PreserveTechnicalTerms);
            }

            // Build the transformation prompt
            string prompt;
            if (request.TransformationType == TransformationType.Translation && !string.IsNullOrEmpty(request.TargetLanguage))
            {
                prompt = TransformationPrompts.BuildTranslationPrompt(
                    request.Text, 
                    request.TargetLanguage,
                    request.PreserveProperNouns,
                    request.PreserveTechnicalTerms);
            }
            else
            {
                prompt = TransformationPrompts.BuildPrompt(request, preservedTerms);
            }

            // Call Llama to transform - use TransformTextAsync which properly handles the request
            var transformationResult = await _llamaService.TransformTextAsync(request);
            var transformedText = transformationResult.TransformedText;

            // Clean up the output
            transformedText = CleanupOutput(transformedText);

            // Calculate basic quality metrics
            QualityMetrics? qualityMetrics = null;
            if (request.IncludeQualityMetrics)
            {
                // Simple similarity calculation (character-based)
                var similarity = CalculateSimilarity(request.Text, transformedText);
                qualityMetrics = new QualityMetrics
                {
                    SimilarityScore = (float)similarity,
                    ConfidenceScore = 0.85f,
                    ReadabilityScore = (float)CalculateReadability(transformedText),
                    MeetsQualityThreshold = similarity >= request.MinQualityThreshold
                };
            }

            // Check if transformation meets quality threshold
            if (qualityMetrics != null && !qualityMetrics.MeetsQualityThreshold && request.MinQualityThreshold > 0)
            {
                LoggingService.Warn($"Transformation quality {qualityMetrics.SimilarityScore} below threshold {request.MinQualityThreshold}");
            }

            var result = new TransformationResult
            {
                OriginalText = request.Text,
                TransformedText = transformedText,
                TransformationType = request.TransformationType,
                Direction = request.Direction,
                QualityMetrics = qualityMetrics,
                Success = true,
                PreservedTerms = preservedTerms,
                ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };

            // Save to history if enabled
            if (_settings.AutoSaveHistory)
            {
                await SaveToHistoryAsync(result);
            }

            return result;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Text transformation failed");
            return new TransformationResult
            {
                OriginalText = request.Text,
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Transforms text using a specific profile.
    /// </summary>
    public async Task<TransformationResult> TransformWithProfileAsync(string text, string profileId)
    {
        var request = new TransformationRequest
        {
            Text = text,
            TransformationType = TransformationType.Grammar,
            Direction = TransformationDirection.Default
        };
        
        return await TransformWithProfileAsync(request, profileId);
    }

    /// <summary>
    /// Transforms text using a specific profile with full request.
    /// </summary>
    public async Task<TransformationResult> TransformWithProfileAsync(TransformationRequest request, string profileId)
    {
        var profile = await GetProfileAsync(profileId);
        if (profile == null)
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = $"Profile '{profileId}' not found",
                OriginalText = request.Text
            };
        }

        // Apply profile settings to request
        request.TransformationType = profile.TransformationType;
        request.Direction = profile.Direction;
        request.PreserveProperNouns = profile.PreserveProperNouns;
        request.PreserveTechnicalTerms = profile.PreserveTechnicalTerms;
        request.MinQualityThreshold = profile.MinQualityThreshold;

        // Merge custom parameters
        if (profile.Parameters.Count > 0)
        {
            request.CustomParameters ??= new Dictionary<string, string>();
            foreach (var param in profile.Parameters)
            {
                request.CustomParameters[param.Key] = param.Value;
            }
        }

        var result = await TransformAsync(request);

        // Update profile usage
        await UpdateProfileUsageAsync(profileId);

        return result;
    }

    /// <summary>
    /// Gets all transformation profiles.
    /// </summary>
    public async Task<IEnumerable<TransformationProfile>> GetProfilesAsync()
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        return _profileCache.Values.ToList();
    }

    /// <summary>
    /// Gets a specific transformation profile.
    /// </summary>
    public async Task<TransformationProfile?> GetProfileAsync(string profileId)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        return _profileCache.TryGetValue(profileId, out var profile) ? profile : null;
    }

    /// <summary>
    /// Creates a new transformation profile.
    /// </summary>
    public async Task<TransformationProfile> CreateProfileAsync(TransformationProfile profile)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        // Validate profile
        var validation = ValidateProfile(profile);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Invalid profile: {string.Join(", ", validation.Errors)}");
        }

        // Set creation timestamps
        profile.Id = Guid.NewGuid().ToString();
        profile.CreatedAt = DateTime.UtcNow;
        profile.ModifiedAt = DateTime.UtcNow;

        // Save to database
        await SaveProfileToDatabaseAsync(profile);

        // Update cache
        _profileCache[profile.Id] = profile;

        LoggingService.Info($"Created transformation profile: {profile.Name}");

        return profile;
    }

    /// <summary>
    /// Updates an existing transformation profile.
    /// </summary>
    public async Task<bool> UpdateProfileAsync(TransformationProfile profile)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        if (!_profileCache.ContainsKey(profile.Id))
        {
            throw new InvalidOperationException($"Profile '{profile.Id}' not found");
        }

        // Validate profile
        var validation = ValidateProfile(profile);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Invalid profile: {string.Join(", ", validation.Errors)}");
        }

        profile.ModifiedAt = DateTime.UtcNow;

        // Save to database
        await SaveProfileToDatabaseAsync(profile);

        // Update cache
        _profileCache[profile.Id] = profile;

        LoggingService.Info($"Updated transformation profile: {profile.Name}");

        return true;
    }

    /// <summary>
    /// Deletes a transformation profile.
    /// </summary>
    public async Task<bool> DeleteProfileAsync(string profileId)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        if (!_profileCache.ContainsKey(profileId))
        {
            throw new InvalidOperationException($"Profile '{profileId}' not found");
        }

        var profile = _profileCache[profileId];
        
        if (profile.IsDefault)
        {
            throw new InvalidOperationException("Cannot delete the default profile");
        }

        // Delete from database
        await DeleteProfileFromDatabaseAsync(profileId);

        // Remove from cache
        _profileCache.Remove(profileId);

        LoggingService.Info($"Deleted transformation profile: {profile.Name}");
        return true;
    }

    /// <summary>
    /// Sets a profile as the default.
    /// </summary>
    public async Task SetDefaultProfileAsync(string profileId)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        if (!_profileCache.ContainsKey(profileId))
        {
            throw new InvalidOperationException($"Profile '{profileId}' not found");
        }

        // Clear existing default
        foreach (var profile in _profileCache.Values)
        {
            if (profile.IsDefault)
            {
                profile.IsDefault = false;
                await SaveProfileToDatabaseAsync(profile);
            }
        }

        // Set new default
        var newDefault = _profileCache[profileId];
        newDefault.IsDefault = true;
        await SaveProfileToDatabaseAsync(newDefault);

        // Update settings
        _settings.DefaultProfileId = profileId;
        await SaveSettingsAsync();

        LoggingService.Info($"Set default profile: {newDefault.Name}");
    }

    /// <summary>
    /// Gets transformation history.
    /// </summary>
    public async Task<IEnumerable<TransformationHistory>> GetHistoryAsync(int limit = 100)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        try
        {
            return _history.OrderByDescending(h => h.Timestamp).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to get transformation history");
            return new List<TransformationHistory>();
        }
    }

    /// <summary>
    /// Clears transformation history.
    /// </summary>
    public async Task<int> ClearHistoryAsync(int olderThanDays = 0)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        try
        {
            int count = 0;
            if (olderThanDays > 0)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
                count = _history.Count(h => h.Timestamp <= cutoffDate);
                _history = _history.Where(h => h.Timestamp > cutoffDate).ToList();
            }
            else
            {
                count = _history.Count;
                _history.Clear();
            }
            LoggingService.Info($"Transformation history cleared: {count} items removed");
            return count;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to clear transformation history");
            return 0;
        }
    }

    /// <summary>
    /// Gets current transformation settings.
    /// </summary>
    public TransformationSettings GetSettings()
    {
        return _settings;
    }

    /// <summary>
    /// Updates transformation settings.
    /// </summary>
    public async Task UpdateSettingsAsync(TransformationSettings settings)
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        await SaveSettingsAsync();

        LoggingService.Info("Transformation settings updated");
    }

    /// <summary>
    /// Validates a transformation profile.
    /// </summary>
    public ValidationResult ValidateProfile(TransformationProfile profile)
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            result.Errors.Add("Profile name is required");
            result.IsValid = false;
        }

        if (profile.MinQualityThreshold < 0 || profile.MinQualityThreshold > 100)
        {
            result.Errors.Add("Quality threshold must be between 0 and 100");
            result.IsValid = false;
        }

        return result;
    }

    #region Private Methods

    private async Task LoadSettingsAsync()
    {
        // Use default settings for now - can be extended to load from settings file
        _settings = new TransformationSettings();
        await Task.CompletedTask;
    }

    private async Task SaveSettingsAsync()
    {
        // Settings are kept in memory - can be extended to persist
        await Task.CompletedTask;
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            // Start with empty cache - profiles can be created in memory
            if (_profileCache.Count == 0)
            {
                var defaultProfile = CreateDefaultProfile();
                _profileCache[defaultProfile.Id] = defaultProfile;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Failed to load transformation profiles: {ex.Message}");
            _profileCache = new Dictionary<string, TransformationProfile>();
        }
    }

    private TransformationProfile CreateDefaultProfile()
    {
        return new TransformationProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Default",
            Description = "Default transformation profile for grammar correction",
            TransformationType = TransformationType.Grammar,
            Direction = TransformationDirection.Default,
            PreserveProperNouns = true,
            PreserveTechnicalTerms = true,
            MinQualityThreshold = 70,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
    }

    private async Task SaveProfileToDatabaseAsync(TransformationProfile profile)
    {
        // Profile is already in _profileCache, just ensure it's there
        _profileCache[profile.Id] = profile;
        await Task.CompletedTask;
    }

    private async Task DeleteProfileFromDatabaseAsync(string profileId)
    {
        // Profile is already removed from _profileCache
        await Task.CompletedTask;
    }

    private async Task UpdateProfileUsageAsync(string profileId)
    {
        if (_profileCache.TryGetValue(profileId, out var profile))
        {
            profile.UsageCount++;
            await SaveProfileToDatabaseAsync(profile);
        }
    }

    private async Task SaveToHistoryAsync(TransformationResult result)
    {
        try
        {
            var history = new TransformationHistory
            {
                OriginalText = result.OriginalText,
                TransformedText = result.TransformedText,
                TransformationType = result.TransformationType,
                Direction = result.Direction,
                QualityScore = result.QualityMetrics?.SimilarityScore ?? 0,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                ProcessingTimeMs = result.ProcessingTimeMs,
                Timestamp = result.Timestamp
            };

            _history.Add(history);

            // Clean up old history
            if (_settings.HistoryRetentionDays > 0)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-_settings.HistoryRetentionDays);
                _history = _history.Where(h => h.Timestamp > cutoffDate).ToList();
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Failed to save transformation history: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Gets supported transformation types.
    /// </summary>
    public IEnumerable<TransformationType> GetSupportedTransformations()
    {
        return new List<TransformationType>
        {
            TransformationType.Grammar,
            TransformationType.Tone,
            TransformationType.Voice,
            TransformationType.Complexity,
            TransformationType.Professionalism,
            TransformationType.PersonalStyle,
            TransformationType.Translation,
            TransformationType.Custom
        };
    }

    /// <summary>
    /// Validates the service configuration.
    /// </summary>
    public async Task<ValidationResult> ValidateConfigurationAsync()
    {
        var result = new ValidationResult { IsValid = true };
        
        if (_llamaService == null)
        {
            result.IsValid = false;
            result.Errors.Add("Llama service is not available");
            return result;
        }
        
        if (!_llamaService.IsLoaded)
        {
            result.IsValid = false;
            result.Errors.Add("Llama model is not loaded");
            return result;
        }
        
        return await Task.FromResult(result);
    }

    /// <summary>
    /// Estimates transformation quality without performing full transformation.
    /// </summary>
    public async Task<QualityMetrics> EstimateQualityAsync(string originalText, string transformedText, TransformationType transformationType)
    {
        var similarity = CalculateSimilarity(originalText, transformedText);
        var readability = CalculateReadability(transformedText);
        
        return await Task.FromResult(new QualityMetrics
        {
            SimilarityScore = (float)similarity,
            ConfidenceScore = 0.85f,
            ReadabilityScore = (float)readability,
            MeetsQualityThreshold = similarity >= DefaultQualityThreshold
        });
    }
    
    private double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0;
            
        // Simple Levenshtein-based similarity
        var len1 = text1.Length;
        var len2 = text2.Length;
        var maxLen = Math.Max(len1, len2);
        
        if (maxLen == 0) return 100;
        
        var distance = LevenshteinDistance(text1, text2);
        return ((double)(maxLen - distance) / maxLen) * 100;
    }
    
    private int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var d = new int[m + 1, n + 1];
        
        for (var i = 0; i <= m; i++) d[i, 0] = i;
        for (var j = 0; j <= n; j++) d[0, j] = j;
        
        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        
        return d[m, n];
    }
    
    private double CalculateReadability(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
            
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;
        
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var sentenceCount = Math.Max(sentences.Length, 1);
        
        var avgWordsPerSentence = (double)words.Length / sentenceCount;
        var avgWordLength = words.Average(w => w.Length);
        
        // Simple readability score based on average words per sentence and word length
        // Lower is generally easier to read
        var score = 100 - (avgWordsPerSentence * 2) - (avgWordLength * 5);
        return Math.Max(0, Math.Min(100, score));
    }

    private List<string> ExtractTermsToPreserve(string text, bool properNouns, bool technicalTerms)
    {
        var terms = new List<string>();

        if (properNouns)
        {
            // Match capitalized words that are likely proper nouns
            var properNounPattern = @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b";
            var properNounMatches = Regex.Matches(text, properNounPattern);
            foreach (Match match in properNounMatches)
            {
                // Filter out common words that start with capital (sentence start)
                if (!IsCommonWord(match.Value))
                {
                    terms.Add(match.Value);
                }
            }
        }

        if (technicalTerms)
        {
            // Match common technical patterns (acronyms, technical terms)
            var technicalPattern = @"\b[A-Z]{2,}(?:\s*[A-Z]{2,})*\b|\b\w+(?:tion|ing|ed|ness|able|ible|ly)\b";
            var technicalMatches = Regex.Matches(text, technicalPattern);
            foreach (Match match in technicalMatches)
            {
                if (!terms.Contains(match.Value))
                {
                    terms.Add(match.Value);
                }
            }
        }

        return terms.Distinct().ToList();
    }

    private bool IsCommonWord(string word)
    {
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "The", "A", "An", "This", "That", "These", "Those",
            "It", "He", "She", "They", "We", "I", "You",
            "Is", "Are", "Was", "Were", "Be", "Been", "Being",
            "Have", "Has", "Had", "Do", "Does", "Did", "Will",
            "Would", "Could", "Should", "May", "Might", "Must",
            "And", "But", "Or", "Not", "In", "On", "At", "To",
            "For", "With", "By", "From", "About", "Into", "Through"
        };

        return commonWords.Contains(word);
    }

    private string CleanupOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        // Remove common artifacts
        output = output.Trim();

        // Remove quotes if the entire output is quoted
        if ((output.StartsWith("\"") && output.EndsWith("\"")) ||
            (output.StartsWith("'") && output.EndsWith("'")))
        {
            output = output.Substring(1, output.Length - 2);
        }

        // Remove any leading/trailing whitespace after quote removal
        output = output.Trim();

        return output;
    }

    #endregion
}
