using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;
using whisperMeOff.Interfaces;
using whisperMeOff.Models.Transformation;

namespace whisperMeOff.Services;

/// <summary>
/// Service for text processing using LLama (LLM) models.
/// Provides text formatting, grammar correction, and translation capabilities.
/// </summary>
/// <remarks>
/// Supports GGUF-formatted models. Uses Vulkan GPU acceleration when available.
/// The service runs inference on background threads to avoid blocking the UI.
/// </remarks>
public class LlamaService : ILlamaService
{
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private StatelessExecutor? _executor;
    private bool _isLoaded;
    private string? _modelPath;
    private readonly object _lock = new();

    /// <summary>
    /// Gets whether a model is currently loaded and ready for inference.
    /// </summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>
    /// Gets the path to the currently loaded model, or null if no model is loaded.
    /// </summary>
    public string? ModelPath => _modelPath;
    
    /// <summary>
    /// Raised when the model loading state changes.
    /// </summary>
    /// <param name="sender">The LlamaService instance.</param>
    /// <param name="isLoaded">True if model is now loaded, false if unloaded.</param>
    public event EventHandler<bool>? ModelLoaded;

    /// <summary>
    /// Initializes and loads a GGUF-formatted LLama model.
    /// </summary>
    /// <param name="modelPath">Path to the GGUF model file.</param>
    /// <remarks>
    /// Validates the model file (size check, GGUF magic number) before loading.
    /// Uses Vulkan GPU acceleration with 35 layers by default.
    /// Shows error dialog to user if loading fails.
    /// </remarks>
    public async Task InitializeAsync(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            LoggingService.Warn($"[LLAMA] Model file not found: {modelPath}");
            _isLoaded = false;
            return;
        }

        try
        {
            _modelPath = modelPath;
            
            // Check file size first
            var fileInfo = new FileInfo(modelPath);
            var fileSizeMb = fileInfo.Length / (1024 * 1024);
            
            if (fileSizeMb < 100)
            {
                throw new Exception($"Model file is too small ({fileSizeMb} MB). The file appears to be incomplete or corrupt.\n" +
                    $"Expected size: At least 500 MB for a 2B parameter model.\n" +
                    $"Please re-download the model.");
            }
            
            // Validate GGUF file magic number
            try
            {
                using var fs = new FileStream(modelPath, FileMode.Open, FileAccess.Read);
                var header = new byte[4];
                fs.Read(header, 0, 4);
                // GGUF files start with "GGUF" (0x46554747 in little endian)
                var magic = BitConverter.ToUInt32(header, 0);
                if (magic != 0x46554747) // "GGUF" in little endian
                {
                    LoggingService.Error($"[LLAMA] Invalid GGUF magic number: 0x{magic:X8}");
                    throw new Exception($"Invalid model file format. The file is not a valid GGUF file.\n" +
                        $"Expected magic number: 0x46554747 (GGUF)\n" +
                        $"Found magic number: 0x{magic:X8}\n" +
                        "This usually means the model was downloaded in the wrong format (e.g., safetensors, pytorch).\n" +
                        "Please download a GGUF-formatted model.");
                }
                LoggingService.Debug($"[LLAMA] GGUF magic number validated: 0x{magic:X8}");
            }
            catch (Exception ex) when (ex.Message.Contains("Invalid model file") || ex.Message.Contains("too small"))
            {
                throw; // Re-throw our validation errors
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"[LLAMA] File validation warning: {ex.Message}");
                // Continue anyway - the main load will fail if truly invalid
            }
            
            // Load model parameters - use GPU (Vulkan) if available
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = 35, // Use Vulkan GPU
                UseMemorymap = true,
            };
            
            LoggingService.Info($"[LLAMA] Loading model from: {modelPath}");
            LoggingService.Info($"[LLAMA] File size: {fileSizeMb} MB");
            
            try
            {
                // Load the model
                _model = await Task.Run(() => LLamaWeights.LoadFromFile(parameters));
                if (_model == null)
                {
                    throw new Exception("Failed to load model weights - returned null");
                }
                
                _context = await Task.Run(() => _model.CreateContext(parameters));
                if (_context == null)
                {
                    throw new Exception("Failed to create model context - returned null");
                }
                
                _executor = new StatelessExecutor(_model, parameters);
                if (_executor == null)
                {
                    throw new Exception("Failed to create executor - returned null");
                }
                
                _isLoaded = true;
                LoggingService.Info($"[LLAMA] Successfully initialized with model: {modelPath}");
                ModelLoaded?.Invoke(this, true);
            }
            catch (LLama.Exceptions.LoadWeightsFailedException loadEx)
            {
                // More specific error handling for weight loading failures
                LoggingService.Warn($"[LLAMA] Weight loading failed: {loadEx.Message}");
                
                string detailedError = $"Failed to load model from: {modelPath}\n\n" +
                    "This can happen if:\n" +
                    "- The model architecture is not supported by this version of LLamaSharp\n" +
                    "- Gemma models from Google are not compatible - use Llama or Mistral instead\n" +
                    "- The model file is corrupted\n\n" +
                    $"Technical error: {loadEx.Message}\n\n" +
                    "Recommended models:\n" +
                    "- llama-2-7b-chat-q4_0.gguf\n" +
                    "- mistral-7b-instruct-v0.2-q4_0.gguf\n" +
                    "- phi-2-q4_0.gguf";
                
                throw new Exception(detailedError, loadEx);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[LLAMA] Initialization error");
            Unload();
            
            // Build error message - use the detailed one from our validation if available
            string errorTitle = "Llama Model Error";
            string errorMessage = ex.Message;
            
            // Notify user of the error
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    errorMessage,
                    errorTitle,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            });
        }
    }

    /// <summary>
    /// Formats and corrects text using the LLama model.
    /// </summary>
    /// <param name="rawText">The raw transcribed text to format.</param>
    /// <returns>Formatted text with corrected spelling and grammar, or original if processing fails.</returns>
    /// <remarks>
    /// Uses a grammar correction prompt with temperature=0 for deterministic results.
    /// Falls back to original text if the result is significantly different or empty.
    /// </remarks>
    public async Task<string> FormatTextAsync(string rawText)
    {
        if (!_isLoaded || _executor == null)
        {
            LoggingService.Debug("[LLAMA] Not loaded, returning raw text");
            return rawText;
        }

        try
        {
            LoggingService.Debug($"[LLAMA] Formatting text: {rawText.Length} chars");
            
            // Simplified prompt format that most models understand better
            var prompt = 
                "Fix spelling and grammar errors in this text. " +
                "Only correct errors, do not change meaning or wording.\n\n" +
                "Input: " + rawText.Trim() + "\n\n" +
                "Corrected:";

            // Add extra anti-prompts to prevent repetition
            var extraAntiPrompts = new List<string>();
            for (int i = 1; i <= 10; i++)
            {
                extraAntiPrompts.Add($"The corrected text is:");
            }
            var allAntiPrompts = new List<string> { "\n\n", "Input:", "Human:", "AI:", "Assistant:", "Question:", "Answer:" }.Concat(extraAntiPrompts).ToList();
            var inferenceParams = new InferenceParams
            {
                MaxTokens = 512,
                AntiPrompts = allAntiPrompts,
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.1f, RepeatPenalty = 1.5f, FrequencyPenalty = 0.5f, PresencePenalty = 0.5f }
            };

            LoggingService.Debug("[LLAMA] Starting inference...");
            LoggingService.Debug($"[LLAMA] Executor is null: {_executor == null}");
            
            // Run inference on background thread to avoid UI thread issues when minimized
            var result = await Task.Run(async () =>
            {
                var response = new System.Text.StringBuilder();
                
                try
                {
                    LoggingService.Debug("[LLAMA] Starting background inference...");
                    
                    await foreach (var text in _executor!.InferAsync(prompt, inferenceParams))
                    {
                        LoggingService.Trace($"[LLAMA] Got token: {text}");
                        response.Append(text);
                        
                        // Stop if we hit a reasonable length
                        if (response.Length > rawText.Length * 3)
                            break;
                    }
                    
                    LoggingService.Debug($"[LLAMA] Background inference complete, got {response.Length} chars");
                }
                catch (Exception ex)
                {
                    LoggingService.Error(ex, "[LLAMA] Inference error - stack trace logged");
                }
                
                return response.ToString();
            });
            
            LoggingService.Debug($"[LLAMA] Task.Run completed, got {result.Length} chars");
            
            var formatted = result.Trim();
            
            LoggingService.Trace($"[LLAMA] Raw output: {formatted}");
            
            // Clean up any common artifacts
            formatted = CleanupLlamaOutput(formatted, rawText);
            
            // Check for null/empty before using formatted
            if (string.IsNullOrEmpty(formatted))
            {
                LoggingService.Debug("[LLAMA] Result is empty, returning raw text");
                return rawText;
            }
            
            LoggingService.Debug($"[LLAMA] Cleaned output: {formatted}");
            LoggingService.Info($"[LLAMA] Inference complete: {formatted.Length} chars");
            
            // If result is empty or too different, fall back to raw text
            if (formatted.Length < rawText.Length * 0.5)
            {
                LoggingService.Debug("[LLAMA] Result too different, returning raw text");
                return rawText;
            }
            
            return formatted;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[LLAMA] Formatting error");
            return rawText;
        }
    }

    /// <summary>
    /// Translates text to the specified target language.
    /// </summary>
    /// <param name="rawText">The text to translate.</param>
    /// <param name="targetLanguage">Target language code (e.g., "en", "es", "fr").</param>
    /// <returns>Translated text, or original if processing fails.</returns>
    /// <remarks>
    /// Supports 26+ languages with built-in name mapping.
    /// Falls back to original text if result is too short or empty.
    /// </remarks>
    public async Task<string> TranslateTextAsync(string rawText, string targetLanguage = "en")
    {
        if (!_isLoaded || _executor == null)
        {
            LoggingService.Debug("[LLAMA] Not loaded, returning raw text");
            return rawText;
        }

        try
        {
            LoggingService.Debug($"[LLAMA] Translating text to {targetLanguage}: {rawText.Length} chars");
            
            // Language name mapping for common languages
            var languageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", "English" },
                { "es", "Spanish" },
                { "fr", "French" },
                { "de", "German" },
                { "it", "Italian" },
                { "pt", "Portuguese" },
                { "ru", "Russian" },
                { "zh", "Chinese" },
                { "ja", "Japanese" },
                { "ko", "Korean" },
                { "ar", "Arabic" },
                { "hi", "Hindi" },
                { "nl", "Dutch" },
                { "pl", "Polish" },
                { "tr", "Turkish" },
                { "vi", "Vietnamese" },
                { "th", "Thai" },
                { "sv", "Swedish" },
                { "da", "Danish" },
                { "no", "Norwegian" },
                { "fi", "Finnish" },
                { "el", "Greek" },
                { "he", "Hebrew" },
                { "id", "Indonesian" },
                { "ms", "Malay" },
                { "uk", "Ukrainian" },
                { "cs", "Czech" },
                { "ro", "Romanian" },
                { "hu", "Hungarian" }
            };
            
            var targetLangName = languageNames.TryGetValue(targetLanguage, out var name) ? name : targetLanguage;
            
            // Translation prompt - clear and direct
            var prompt = 
                $"Translate the following text to {targetLangName}. " +
                "Provide only the translation, nothing else.\n\n" +
                "Input: " + rawText.Trim() + "\n\n" +
                $"Translation in {targetLangName}:";

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 512,
                AntiPrompts = new List<string> { "\n\n", "Input:", "Human:", "AI:", "Assistant:", "Question:", "Answer:" },
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.1f, RepeatPenalty = 1.0f }
            };

            LoggingService.Debug("[LLAMA] Starting translation inference...");
            
            // Run inference on background thread to avoid UI thread issues when minimized
            var result = await Task.Run(async () =>
            {
                var response = new System.Text.StringBuilder();
                
                try
                {
                    LoggingService.Debug("[LLAMA] Starting background translation inference...");
                    
                    await foreach (var text in _executor!.InferAsync(prompt, inferenceParams))
                    {
                        LoggingService.Trace($"[LLAMA] Got token: {text}");
                        response.Append(text);
                        
                        // Stop if we hit a reasonable length
                        if (response.Length > rawText.Length * 4)
                            break;
                    }
                    
                    LoggingService.Debug($"[LLAMA] Background translation complete, got {response.Length} chars");
                }
                catch (Exception ex)
                {
                    LoggingService.Error(ex, "[LLAMA] Translation inference error - stack trace logged");
                }
                
                return response.ToString();
            });
            
            LoggingService.Debug($"[LLAMA] Translation Task.Run completed, got {result.Length} chars");
            
            var translated = result.Trim();
            
            LoggingService.Trace($"[LLAMA] Raw translation output: {translated}");
            
            // Clean up any common artifacts
            translated = CleanupLlamaOutput(translated, rawText);
            
            // Check for null/empty before using translated
            if (string.IsNullOrEmpty(translated))
            {
                LoggingService.Debug("[LLAMA] Translation result is empty, returning raw text");
                return rawText;
            }
            
            LoggingService.Debug($"[LLAMA] Cleaned translation output: {translated}");
            LoggingService.Info($"[LLAMA] Translation complete: {translated.Length} chars");
            
            // If result is empty or too short, fall back to raw text
            if (translated.Length < 3)
            {
                LoggingService.Debug("[LLAMA] Translation result too short, returning raw text");
                return rawText;
            }
            
            return translated;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[LLAMA] Translation error");
            return rawText;
        }
    }
    
    private string CleanupLlamaOutput(string output, string original)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;

        // If output is very short or looks like it was cut off, try to use it as-is
        if (output.Length < 50 || !output.Contains('\n'))
            return output.Trim();

        var lines = output.Split('\n');
        var cleanedLines = new List<string>();
        bool foundRealContent = false;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Skip empty lines at the start
            if (string.IsNullOrWhiteSpace(trimmed) && !foundRealContent)
                continue;
            
            // Skip lines that are clearly prompt fragments or instructions
            // But be careful not to skip valid output that happens to contain similar words
            if (!foundRealContent)
            {
                // Skip obvious prompt headers/instructions
                if (trimmed.StartsWith("Fix any") || 
                    trimmed.StartsWith("Correct any") ||
                    trimmed.StartsWith("Grammar:") ||
                    trimmed.StartsWith("Text to transform:") ||
                    trimmed.StartsWith("Input:") ||
                    trimmed.StartsWith("Original:") ||
                    trimmed.StartsWith("Here is") ||
                    trimmed.StartsWith("Sure,") ||
                    trimmed.StartsWith("Here's") ||
                    trimmed.StartsWith("The") && trimmed.Length < 30 ||  // "The following..."
                    trimmed.StartsWith("Do NOT") ||
                    trimmed.StartsWith("Human:") ||
                    trimmed.StartsWith("Assistant:") ||
                    trimmed.StartsWith("AI:") ||
                    trimmed.StartsWith("Result:") ||
                    trimmed.StartsWith("Corrected:") ||
                    trimmed.StartsWith("Output:") ||
                    trimmed.StartsWith("Transform"))
                {
                    continue;
                }
            }
            
            // After finding content, collect all subsequent non-empty lines
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                foundRealContent = true;
                cleanedLines.Add(line);
            }
            else if (foundRealContent)
            {
                // Preserve blank lines within content
                cleanedLines.Add(line);
            }
        }
        
        var result = string.Join("\n", cleanedLines).Trim();
        
        // If we filtered everything out, return original output
        if (string.IsNullOrWhiteSpace(result))
            return output.Trim();
        
        // If result is suspiciously short compared to input, might be missing content
        if (result.Length < original.Length * 0.3)
        {
            // Try to use more of the original output
            return output.Trim();
        }
        
        return result;
    }

    /// <summary>
    /// Transforms text based on the specified transformation request.
    /// </summary>
    /// <param name="request">The transformation request containing text and transformation type.</param>
    /// <returns>A transformation result containing the transformed text and quality metrics.</returns>
    public async Task<TransformationResult> TransformTextAsync(TransformationRequest request)
    {
        if (!_isLoaded)
        {
            return new TransformationResult
            {
                OriginalText = request.Text,
                TransformedText = string.Empty,
                Success = false,
                ErrorMessage = "Model not loaded"
            };
        }

        try
        {
            // Build the prompt using BuildPrompt to include preservation instructions
            var prompt = TransformationPrompts.BuildPrompt(request);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = Math.Max(512, request.Text.Length / 2),
                AntiPrompts = new List<string> { "Human:", "User:", "\n\n" },
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.3f, RepeatPenalty = 1.0f }
            };

            var result = await Task.Run(async () =>
            {
                var response = new System.Text.StringBuilder();
                await foreach (var text in _executor!.InferAsync(prompt, inferenceParams))
                {
                    response.Append(text);
                    if (response.Length > request.Text.Length * 3)
                        break;
                }
                return response.ToString();
            });

            var transformed = CleanupLlamaOutput(result, request.Text);
            
            // Calculate quality metrics
            var metrics = CalculateQualityMetrics(request.Text, transformed);

            return new TransformationResult
            {
                OriginalText = request.Text,
                TransformedText = transformed,
                Success = true,
                TransformationType = request.TransformationType,
                QualityMetrics = metrics,
                ProcessingTimeMs = 0 // Could add timing if needed
            };
        }
        catch (Exception ex)
        {
            return new TransformationResult
            {
                OriginalText = request.Text,
                TransformedText = string.Empty,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Transforms text using a predefined transformation profile.
    /// </summary>
    /// <param name="text">The text to transform.</param>
    /// <param name="profile">The transformation profile to use.</param>
    /// <returns>A transformation result containing the transformed text and quality metrics.</returns>
    public async Task<TransformationResult> TransformWithProfileAsync(string text, TransformationProfile profile)
    {
        if (!_isLoaded)
        {
            return new TransformationResult
            {
                OriginalText = text,
                TransformedText = string.Empty,
                Success = false,
                ErrorMessage = "Model not loaded"
            };
        }

        try
        {
            // Build prompt from profile settings
            var prompt = BuildProfilePrompt(text, profile);
            
            var inferenceParams = new InferenceParams
            {
                MaxTokens = Math.Max(512, text.Length / 2),
                AntiPrompts = new List<string> { "Human:", "User:", "\n\n" },
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.3f, RepeatPenalty = 1.0f }
            };

            var result = await Task.Run(async () =>
            {
                var response = new System.Text.StringBuilder();
                await foreach (var t in _executor!.InferAsync(prompt, inferenceParams))
                {
                    response.Append(t);
                    if (response.Length > text.Length * 3)
                        break;
                }
                return response.ToString();
            });

            var transformed = CleanupLlamaOutput(result, text);
            
            // Calculate quality metrics
            var metrics = CalculateQualityMetrics(text, transformed);

            return new TransformationResult
            {
                OriginalText = text,
                TransformedText = transformed,
                Success = true,
                TransformationType = TransformationType.PersonalStyle,
                QualityMetrics = metrics,
                ProcessingTimeMs = 0
            };
        }
        catch (Exception ex)
        {
            return new TransformationResult
            {
                OriginalText = text,
                TransformedText = string.Empty,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Applies multiple transformations to the same text sequentially.
    /// </summary>
    /// <param name="text">The text to transform.</param>
    /// <param name="transformations">List of transformation requests to apply in order.</param>
    /// <returns>A transformation result containing the final transformed text and aggregated quality metrics.</returns>
    public async Task<TransformationResult> TransformBatchAsync(string text, List<TransformationRequest> transformations)
    {
        if (!_isLoaded)
        {
            return new TransformationResult
            {
                OriginalText = text,
                TransformedText = string.Empty,
                Success = false,
                ErrorMessage = "Model not loaded"
            };
        }

        var currentText = text;
        var allMetrics = new List<QualityMetrics>();
        
        foreach (var request in transformations)
        {
            request.Text = currentText;
            var result = await TransformTextAsync(request);
            
            if (!result.Success)
            {
                return new TransformationResult
                {
                    OriginalText = text,
                    TransformedText = currentText,
                    Success = false,
                    ErrorMessage = $"Transformation failed at {request.TransformationType}: {result.ErrorMessage}"
                };
            }
            
            currentText = result.TransformedText;
            allMetrics.Add(result.QualityMetrics);
        }

        // Calculate aggregate metrics
        var aggregateMetrics = AggregateQualityMetrics(allMetrics);

        return new TransformationResult
        {
            OriginalText = text,
            TransformedText = currentText,
            Success = true,
            TransformationType = TransformationType.Custom,
            QualityMetrics = aggregateMetrics,
            ProcessingTimeMs = 0
        };
    }

    /// <summary>
    /// Gets the list of supported transformation types.
    /// </summary>
    /// <returns>List of supported transformation types.</returns>
    public List<TransformationType> GetSupportedTransformations()
    {
        return new List<TransformationType>
        {
            TransformationType.Tone,
            TransformationType.Voice,
            TransformationType.Complexity,
            TransformationType.Professionalism,
            TransformationType.PersonalStyle,
            TransformationType.Grammar,
            TransformationType.Translation,
            TransformationType.Custom
        };
    }

    /// <summary>
    /// Estimates the number of tokens needed for a given text.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <returns>Estimated token count.</returns>
    public int EstimateTokens(string text)
    {
        // Rough estimate: 1 token ≈ 4 characters for English text
        // This is a simplified estimation; actual tokenization varies by model
        return text.Length / 4;
    }

    /// <summary>
    /// Builds a transformation prompt from a transformation profile.
    /// </summary>
    private string BuildProfilePrompt(string text, TransformationProfile profile)
    {
        var promptBuilder = new System.Text.StringBuilder();
        
        promptBuilder.AppendLine("You are a text transformation assistant.");
        promptBuilder.AppendLine("Transform the following text according to the specified requirements:");
        promptBuilder.AppendLine();
        
        // Add transformation settings from profile parameters
        if (profile.Parameters != null && profile.Parameters.Count > 0)
        {
            foreach (var param in profile.Parameters)
            {
                promptBuilder.AppendLine($"- {param.Key}: {param.Value}");
            }
            promptBuilder.AppendLine();
        }
        
        // Add transformation description
        if (!string.IsNullOrEmpty(profile.Description))
        {
            promptBuilder.AppendLine($"Transformation goal: {profile.Description}");
            promptBuilder.AppendLine();
        }
        
        promptBuilder.AppendLine("- Preserve the original meaning as much as possible");
        promptBuilder.AppendLine("- Maintain grammatical correctness");
        
        if (profile.PreserveProperNouns)
        {
            promptBuilder.AppendLine("- Preserve proper nouns (names, places, organizations)");
        }
        
        if (profile.PreserveTechnicalTerms)
        {
            promptBuilder.AppendLine("- Preserve technical terminology");
        }
        
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Text to transform:");
        promptBuilder.AppendLine(text);
        
        return promptBuilder.ToString();
    }

    private QualityMetrics CalculateQualityMetrics(string original, string transformed)
    {
        // Simple similarity calculation using Levenshtein-like approach
        var similarity = CalculateSimilarity(original, transformed);
        
        // Estimate confidence based on length difference
        var lengthRatio = original.Length > 0
            ? (double)transformed.Length / original.Length
            : 1.0;
        var confidence = Math.Max(0, Math.Min(1, 1 - Math.Abs(1 - lengthRatio) * 0.5));
        
        // Calculate readability (simplified Flesch-like score)
        var readability = CalculateReadabilityScore(transformed);
        
        // Structure preservation (based on sentence count ratio)
        var originalSentences = original.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var transformedSentences = transformed.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var structureScore = originalSentences > 0
            ? Math.Min(1.0, (double)transformedSentences / originalSentences)
            : 1.0;

        // Calculate overall score
        var overall = (similarity + confidence + readability + structureScore) / 4.0;

        return new QualityMetrics
        {
            SimilarityScore = (int)(similarity * 100),
            ConfidenceScore = (int)(confidence * 100),
            ReadabilityScore = (int)(readability * 100),
            StructureScore = (int)(structureScore * 100),
            OverallScore = (int)(overall * 100)
        };
    }

    private double CalculateSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return s1 == s2 ? 1.0 : 0.0;

        int distance = LevenshteinDistance(s1, s2);
        int maxLen = Math.Max(s1.Length, s2.Length);
        return maxLen > 0 ? 1.0 - ((double)distance / maxLen) : 1.0;
    }

    private int LevenshteinDistance(string s1, string s2)
    {
        int[,] d = new int[s1.Length + 1, s2.Length + 1];
        
        for (int i = 0; i <= s1.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) d[0, j] = j;
        
        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        
        return d[s1.Length, s2.Length];
    }

    private double CalculateReadabilityScore(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (words.Length == 0 || sentences.Length == 0)
            return 0.5;

        // Simplified Flesch Reading Ease approximation
        double avgWordsPerSentence = words.Length / (double)sentences.Length;
        double avgCharsPerWord = text.Replace(" ", "").Length / (double)words.Length;
        
        // Normalize to 0-1 scale (typical scores range 0-100)
        double score = 1.0 - ((avgWordsPerSentence / 20.0) * 0.5 + (avgCharsPerWord / 10.0) * 0.5);
        return Math.Max(0, Math.Min(1, score));
    }

    private QualityMetrics AggregateQualityMetrics(List<QualityMetrics> metrics)
    {
        if (metrics.Count == 0)
        {
            return new QualityMetrics
            {
                SimilarityScore = 0,
                ConfidenceScore = 0,
                ReadabilityScore = 0,
                StructureScore = 0,
                OverallScore = 0
            };
        }

        var avgSimilarity = metrics.Average(m => m.SimilarityScore);
        var avgConfidence = metrics.Average(m => m.ConfidenceScore);
        var avgReadability = metrics.Average(m => m.ReadabilityScore);
        var avgStructure = metrics.Average(m => m.StructureScore);
        var avgOverall = metrics.Average(m => m.OverallScore);

        return new QualityMetrics
        {
            SimilarityScore = (int)avgSimilarity,
            ConfidenceScore = (int)avgConfidence,
            ReadabilityScore = (int)avgReadability,
            StructureScore = (int)avgStructure,
            OverallScore = (int)avgOverall
        };
    }

    /// <summary>
    /// Cleans up model resources and unloads the model from memory.
    /// </summary>
    /// <remarks>
    /// Thread-safe operation that disposes the context, executor, and model weights.
    /// </remarks>
    public void Unload()
    {
        lock (_lock)
        {
            _executor = null;
            _context?.Dispose();
            _context = null;
            _model?.Dispose();
            _model = null;
            _isLoaded = false;
            _modelPath = null;
            
            ModelLoaded?.Invoke(this, false);
        }
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}
