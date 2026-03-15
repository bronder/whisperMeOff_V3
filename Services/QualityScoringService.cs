using whisperMeOff.Models.Transformation;
using whisperMeOff.Services;

namespace whisperMeOff.Services;

/// <summary>
/// Service for calculating quality metrics on text transformations
/// </summary>
public class QualityScoringService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    
    // Weights for quality components
    private const float SimilarityWeight = 0.35f;
    private const float ConfidenceWeight = 0.25f;
    private const float ReadabilityWeight = 0.25f;
    private const float StructureWeight = 0.15f;
    
    // Quality thresholds (on 0-100 scale)
    private const float MinimumQualityThreshold = 60f;
    private const float HighQualityThreshold = 85f;
    
    /// <summary>
    /// Calculate comprehensive quality metrics for a transformation
    /// </summary>
    public QualityMetrics CalculateMetrics(string originalText, string transformedText, float? modelConfidence = null)
    {
        Logger.Debug("Calculating quality metrics for transformation");
        
        var similarityScore = CalculateSimilarity(originalText, transformedText);
        var readabilityScore = CalculateReadability(transformedText);
        var structureScore = CalculateStructureScore(originalText, transformedText);
        
        // Use provided confidence or estimate based on transformation quality
        var confidenceScore = modelConfidence ?? EstimateConfidence(similarityScore, readabilityScore);
        
        // Calculate overall quality score
        var overallScore = (similarityScore * SimilarityWeight) +
                          (confidenceScore * ConfidenceWeight) +
                          (readabilityScore * ReadabilityWeight) +
                          (structureScore * StructureWeight);
        
        // Convert to 0-100 scale for QualityMetrics (multiply by 100)
        var metrics = new QualityMetrics
        {
            SimilarityScore = similarityScore * 100f,
            ConfidenceScore = confidenceScore * 100f,
            ReadabilityScore = readabilityScore * 100f,
            StructureScore = structureScore * 100f,
            OverallScore = overallScore * 100f,
            MeetsQualityThreshold = (overallScore * 100f) >= MinimumQualityThreshold
        };
        
        Logger.Debug($"Quality metrics calculated: Overall={overallScore:F3}, Similarity={similarityScore:F3}, Readability={readabilityScore:F3}");
        
        return metrics;
    }
    
    /// <summary>
    /// Calculate semantic similarity between original and transformed text
    /// Uses simple token-based comparison with weighting
    /// </summary>
    private float CalculateSimilarity(string original, string transformed)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(transformed))
            return 0f;
        
        var originalTokens = Tokenize(original);
        var transformedTokens = Tokenize(transformed);
        
        if (originalTokens.Count == 0 || transformedTokens.Count == 0)
            return 0f;
        
        // Calculate Jaccard similarity
        var intersection = originalTokens.Intersect(transformedTokens).Count();
        var union = originalTokens.Union(transformedTokens).Count();
        
        var jaccardSimilarity = union > 0 ? (float)intersection / union : 0f;
        
        // Also check for key content words preservation
        var keyContentWords = ExtractKeyContentWords(originalTokens);
        var preservedKeyWords = keyContentWords.Intersect(transformedTokens).Count();
        var keyWordPreservation = keyContentWords.Count > 0 
            ? (float)preservedKeyWords / keyContentWords.Count 
            : 1f;
        
        // Weighted combination: 60% Jaccard, 40% key word preservation
        return (jaccardSimilarity * 0.6f) + (keyWordPreservation * 0.4f);
    }
    
    /// <summary>
    /// Calculate readability score based on sentence structure and length
    /// </summary>
    private float CalculateReadability(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0f;
        
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (sentences.Length == 0)
            return 0f;
        
        // Average sentence length (prefer 10-20 words)
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var avgSentenceLength = words.Length / (float)sentences.Length;
        
        // Ideal sentence length is around 15-20 words
        var sentenceLengthScore = avgSentenceLength switch
        {
            >= 10 and <= 20 => 1.0f,
            >= 8 and < 10 => 0.9f,
            >= 5 and < 8 => 0.75f,
            >= 20 and < 30 => 0.7f,
            >= 30 => 0.5f,
            _ => 0.3f
        };
        
        // Check for excessive repetition
        var repetitionPenalty = CalculateRepetitionPenalty(text);
        
        // Check for proper punctuation and capitalization
        var punctuationScore = CalculatePunctuationScore(text);
        
        return (sentenceLengthScore * 0.5f) + (repetitionPenalty * 0.3f) + (punctuationScore * 0.2f);
    }
    
    /// <summary>
    /// Calculate score based on text structure preservation
    /// </summary>
    private float CalculateStructureScore(string original, string transformed)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(transformed))
            return 0f;
        
        var originalParagraphs = original.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var transformedParagraphs = transformed.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        // Paragraph structure preservation
        var paragraphScore = originalParagraphs.Length == transformedParagraphs.Length 
            ? 1.0f 
            : Math.Max(0.5f, 1.0f - Math.Abs(originalParagraphs.Length - transformedParagraphs.Length) * 0.2f);
        
        // Sentence count preservation
        var originalSentences = CountSentences(original);
        var transformedSentences = CountSentences(transformed);
        var sentenceRatio = originalSentences > 0 
            ? Math.Min(originalSentences, transformedSentences) / (float)Math.Max(originalSentences, 1)
            : 0f;
        
        // Preserve lists and structured content
        var listScore = PreserveListStructure(original, transformed);
        
        return (paragraphScore * 0.4f) + (sentenceRatio * 0.4f) + (listScore * 0.2f);
    }
    
    /// <summary>
    /// Estimate confidence score based on other metrics
    /// </summary>
    private float EstimateConfidence(float similarity, float readability)
    {
        // Higher similarity and readability indicates more confident transformation
        return Math.Min(1.0f, (similarity + readability) / 1.5f);
    }
    
    /// <summary>
    /// Tokenize text into words
    /// </summary>
    private HashSet<string> Tokenize(string text)
    {
        var tokens = text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']' }, 
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1) // Filter out single characters
            .ToHashSet();
        
        return tokens;
    }
    
    /// <summary>
    /// Extract key content words (nouns, verbs, adjectives)
    /// </summary>
    private HashSet<string> ExtractKeyContentWords(HashSet<string> tokens)
    {
        // Filter out common stop words to get key content
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "must", "can", "to", "of", "in", "for",
            "on", "with", "at", "by", "from", "as", "into", "through", "during",
            "before", "after", "above", "below", "between", "under", "again",
            "further", "then", "once", "here", "there", "when", "where", "why",
            "how", "all", "each", "few", "more", "most", "other", "some", "such",
            "no", "nor", "not", "only", "own", "same", "so", "than", "too", "very",
            "just", "and", "but", "or", "if", "because", "until", "while"
        };
        
        return tokens.Where(t => !stopWords.Contains(t)).ToHashSet();
    }
    
    /// <summary>
    /// Calculate penalty for excessive repetition
    /// </summary>
    private float CalculateRepetitionPenalty(string text)
    {
        var words = text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (words.Length < 5)
            return 1.0f;
        
        // Check for consecutive repetition
        var consecutiveRepeats = 0;
        for (int i = 1; i < words.Length; i++)
        {
            if (words[i] == words[i - 1])
                consecutiveRepeats++;
        }
        
        var repeatRatio = consecutiveRepeats / (float)words.Length;
        
        // Lower score for higher repetition
        return Math.Max(0.0f, 1.0f - (repeatRatio * 10));
    }
    
    /// <summary>
    /// Calculate score based on proper punctuation
    /// </summary>
    private float CalculatePunctuationScore(string text)
    {
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (sentences.Length == 0)
            return 0.5f;
        
        // Check if sentences have proper spacing after punctuation
        var properSpacing = text.Contains(". ") || text.Contains("! ") || text.Contains("? ");
        
        // Check for capitalization after sentences
        var hasCapitalization = sentences.Length > 0 && sentences.Any(s => !string.IsNullOrEmpty(s) && char.IsUpper(s[0]));
        
        return (properSpacing ? 0.5f : 0f) + (hasCapitalization ? 0.5f : 0f);
    }
    
    /// <summary>
    /// Count sentences in text
    /// </summary>
    private int CountSentences(string text)
    {
        return text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
    
    /// <summary>
    /// Check if list structure is preserved
    /// </summary>
    private float PreserveListStructure(string original, string transformed)
    {
        // Check for bullet points, numbers, or list markers
        var listMarkers = new[] { "•", "-", "*", "1.", "2.", "3.", "4.", "5." };
        
        var originalHasList = listMarkers.Any(m => original.Contains(m));
        var transformedHasList = listMarkers.Any(m => transformed.Contains(m));
        
        if (originalHasList && !transformedHasList)
            return 0.3f; // Penalty for losing list structure
        if (!originalHasList && transformedHasList)
            return 0.8f; // Bonus for adding structure
        if (originalHasList && transformedHasList)
            return 1.0f; // Good preservation
        
        return 0.9f; // Neutral - no lists in either
    }
    
    /// <summary>
    /// Check if transformation meets minimum quality requirements
    /// </summary>
    public bool MeetsQualityThreshold(QualityMetrics metrics)
    {
        return metrics?.MeetsQualityThreshold ?? false;
    }
    
    /// <summary>
    /// Determine if transformation is high quality
    /// </summary>
    public bool IsHighQuality(QualityMetrics metrics)
    {
        return metrics?.OverallScore >= HighQualityThreshold;
    }
    
    /// <summary>
    /// Get quality rating description
    /// </summary>
    public string GetQualityRating(QualityMetrics metrics)
    {
        if (metrics == null)
            return "Unknown";
        
        return metrics.OverallScore switch
        {
            >= 0.9f => "Excellent",
            >= 0.8f => "Very Good",
            >= 0.7f => "Good",
            >= 0.6f => "Acceptable",
            >= 0.5f => "Fair",
            _ => "Poor"
        };
    }
    
    /// <summary>
    /// Compare two transformations and return the better one
    /// </summary>
    public QualityMetrics SelectBetter(QualityMetrics a, QualityMetrics b)
    {
        if (a == null) return b;
        if (b == null) return a;
        
        return a.OverallScore >= b.OverallScore ? a : b;
    }
}
