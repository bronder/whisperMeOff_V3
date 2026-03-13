using System.IO;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace whisperMeOff.Services;

/// <summary>
/// Service for text processing using LLama (LLM) models.
/// Provides text formatting, grammar correction, and translation capabilities.
/// </summary>
/// <remarks>
/// Supports GGUF-formatted models. Uses Vulkan GPU acceleration when available.
/// The service runs inference on background threads to avoid blocking the UI.
/// </remarks>
public class LlamaService : IDisposable
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

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 256,
                AntiPrompts = new List<string> { "\n\n", "Input:", "Human:", "AI:", "Assistant:", "Question:", "Answer:" },
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.0f, RepeatPenalty = 1.0f }
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
        // Remove common Llama output artifacts
        var lines = output.Split('\n');
        var cleanedLines = new List<string>();
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Skip lines that look like prompts or instructions
            if (trimmed.StartsWith("Result:") || trimmed.StartsWith("Fix") || 
                trimmed.StartsWith("Spelling") || trimmed.StartsWith("Grammar") ||
                trimmed.StartsWith("Input") || trimmed.StartsWith("Output") ||
                trimmed.StartsWith("* ") || trimmed.StartsWith("- ") ||
                trimmed.StartsWith("# ") || trimmed.StartsWith("1.") ||
                trimmed.StartsWith("Here is") || trimmed.StartsWith("Sure,") ||
                trimmed.StartsWith("Here's") || trimmed.StartsWith("Human:") ||
                trimmed.StartsWith("AI:") || trimmed.StartsWith("Assistant:"))
            {
                continue;
            }
            
            cleanedLines.Add(line);
        }
        
        var result = string.Join("\n", cleanedLines).Trim();
        
        // If output is significantly longer than input, it's probably adding unwanted content
        // In that case, try to find where actual new content starts
        if (result.Length > original.Length * 2)
        {
            // Look for common patterns where corrections might start
            var lowerResult = result.ToLowerInvariant();
            var patterns = new[] { "corrected:", "fixed:", "output:", "result:", "text:" };
            
            foreach (var pattern in patterns)
            {
                var idx = lowerResult.IndexOf(pattern);
                if (idx >= 0 && idx < result.Length / 2) // Found in first half
                {
                    result = result.Substring(idx + pattern.Length).Trim();
                    break;
                }
            }
        }
        
        return result;
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
