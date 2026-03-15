using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace whisperMeOff.Services;

/// <summary>
/// Service for speech-to-text transcription using Whisper.net library.
/// Supports multiple runtime backends (Vulkan, CUDA, CPU) and various transcription options.
/// </summary>
/// <remarks>
/// The service supports:
/// - Multiple Whisper model sizes (tiny, base, small, medium, large)
/// - Custom vocabulary prompts
/// - Word replacement rules
/// - Multiple language support with auto-detection
/// - Translation mode (when supported by the model)
/// </remarks>
public class WhisperService : IDisposable
{
    private bool _isInitialized;
    private string? _modelPath;
    private WhisperFactory? _factory;

    /// <summary>
    /// Gets whether the Whisper service has been initialized with a model.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Initializes the Whisper service and loads the configured model.
    /// </summary>
    /// <remarks>
    /// Attempts to load models in order of preference: Vulkan (GPU) > CUDA > CPU.
    /// Falls back to CPU if GPU acceleration is unavailable.
    /// </remarks>
    public async Task InitializeAsync()
    {
        try
        {
            var modelPath = GetModelPath();
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                modelPath = Path.Combine(App.WhisperModelsPath, "ggml-small.bin");
            }

            if (File.Exists(modelPath))
            {
                _modelPath = modelPath;
                
                // Set runtime order: try Vulkan first (GPU), then CUDA, then CPU
                RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cuda, RuntimeLibrary.Cuda12, RuntimeLibrary.Cpu];
                LoggingService.Info("[Whisper] ========== MODEL LOADING ==========");
                LoggingService.Info($"[Whisper] Model file: {Path.GetFullPath(_modelPath)}");
                LoggingService.Info($"[Whisper] Model size: {new FileInfo(_modelPath).Length / 1024 / 1024} MB");
                LoggingService.Debug("[Whisper] Runtime order: Vulkan -> CUDA -> CPU");
                
                // Add logger to capture Whisper.net library loading messages
                using var whisperLogger = Whisper.net.Logger.LogProvider.AddLogger((level, message) =>
                {
                    LoggingService.Debug($"[Whisper Lib] {level}: {message}");
                });

                _factory = WhisperFactory.FromPath(modelPath);
                LoggingService.Info("[Whisper] Factory loaded successfully");
                
                _isInitialized = true;
            }
            else
            {
                LoggingService.Warn("Whisper model not found");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Whisper initialization error");
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Gets the path to the currently configured Whisper model.
    /// </summary>
    /// <returns>Path to the model file, or empty string if no model is configured.</returns>
    /// <remarks>
    /// Checks custom path from settings first, then falls back to default location.
    /// </remarks>
    public string GetModelPath()
    {
        if (!string.IsNullOrEmpty(App.Settings.Whisper.ModelPath) && File.Exists(App.Settings.Whisper.ModelPath))
        {
            return App.Settings.Whisper.ModelPath;
        }

        // Check default locations
        var defaultPath = Path.Combine(App.WhisperModelsPath, "ggml-small.bin");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        return "";
    }

    /// <summary>
    /// Transcribes an audio file to text using Whisper.
    /// </summary>
    /// <param name="audioPath">Path to the audio file to transcribe (WAV format).</param>
    /// <returns>Transcribed text, or empty string if no speech was detected.</returns>
    /// <exception cref="InvalidOperationException">Thrown when service is not initialized.</exception>
    /// <exception cref="FileNotFoundException">Thrown when model or audio file is not found.</exception>
    /// <remarks>
    /// Applies the following post-processing:
    /// - Custom vocabulary from settings (used as initial prompt)
    /// - Word replacement rules from settings
    /// </remarks>
    public async Task<string> TranscribeAsync(string audioPath)
    {
        if (!_isInitialized || _factory == null)
        {
            throw new InvalidOperationException("Whisper not initialized");
        }

        try
        {
            var modelPath = GetModelPath();
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                throw new FileNotFoundException("Whisper model not found");
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException($"Audio file not found: {audioPath}");
            }

            LoggingService.Info($"Starting transcription of: {audioPath}");

            // Get language setting
            var language = App.Settings.Whisper.Language;
            
            // Get custom vocabulary if set
            var customVocabulary = App.Settings.Whisper.CustomVocabulary;
            
            // Build processor with options
            var builder = _factory.CreateBuilder()
                .WithLanguage(language);
            
            // Try to enable translation if requested - use reflection to find the method
            if (App.Settings.Whisper.Translate)
            {
                try
                {
                    // Try to find and invoke WithTask method
                    var builderType = builder.GetType();
                    var withTaskMethod = builderType.GetMethod("WithTask");
                    if (withTaskMethod != null)
                    {
                        // Look for WhisperTask enum
                        var whisperTaskType = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => a.GetTypes())
                            .FirstOrDefault(t => t.Name == "WhisperTask");
                        
                        if (whisperTaskType != null)
                        {
                            var translateValue = Enum.GetValues(whisperTaskType)
                                .Cast<object>()
                                .FirstOrDefault(v => v.ToString() == "Translate");
                            
                            if (translateValue != null)
                            {
                                withTaskMethod.Invoke(builder, new[] { translateValue });
                                LoggingService.Info("[Whisper] Translation enabled via WithTask");
                            }
                        }
                    }
                    else
                    {
                        LoggingService.Warn("[Whisper] WithTask method not found - translation not available");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Warn($"[Whisper] Translation setup failed: {ex.Message}");
                }
            }

            // Apply custom vocabulary as initial prompt if set
            // Using dynamic to support different Whisper.net versions
            if (!string.IsNullOrWhiteSpace(customVocabulary))
            {
                try
                {
                    dynamic dynamicBuilder = builder;
                    dynamicBuilder = dynamicBuilder.WithInitialPrompt(customVocabulary);
                    builder = dynamicBuilder;
                    LoggingService.Debug($"[Whisper] Using custom vocabulary: {customVocabulary}");
                }
                catch (Exception ex)
                {
                    LoggingService.Warn($"[Whisper] Custom vocabulary not supported in this version: {ex.Message}");
                }
            }

            using var processor = builder.Build();

            // Open the audio file and process it
            using var fileStream = File.OpenRead(audioPath);
            
            var results = new List<string>();
            await foreach (var r in processor.ProcessAsync(fileStream, CancellationToken.None))
            {
                if (!string.IsNullOrWhiteSpace(r.Text))
                {
                    results.Add(r.Text);
                }
            }

            var transcription = string.Join(" ", results).Trim();
            
            // Apply word replacements
            var replacements = App.Settings.Whisper.WordReplacements;
            if (replacements.Any())
            {
                foreach (var replacement in replacements)
                {
                    if (!string.IsNullOrEmpty(replacement.Source) && !string.IsNullOrEmpty(replacement.Replacement))
                    {
                        // Case-insensitive replacement
                        transcription = System.Text.RegularExpressions.Regex.Replace(
                            transcription, 
                            System.Text.RegularExpressions.Regex.Escape(replacement.Source), 
                            replacement.Replacement, 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }
                }
                LoggingService.Info($"[Whisper] Applied {replacements.Count} word replacements");
            }
            
            if (!string.IsNullOrEmpty(transcription))
            {
                LoggingService.Info($"[Whisper] Transcription complete: {transcription}");
            }
            else
            {
                LoggingService.Debug("[Whisper] Transcription complete (empty)");
            }
            return transcription;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Transcription error");
            throw;
        }
    }

    /// <summary>
    /// Reloads the Whisper model from a new path.
    /// </summary>
    /// <param name="modelPath">Path to the new model file.</param>
    /// <remarks>
    /// This is an asynchronous operation that disposes the current model
    /// and loads the new one in the background.
    /// </remarks>
    public void ReloadModel(string modelPath)
    {
        _isInitialized = false;
        _factory?.Dispose();
        _factory = null;

        App.Settings.Whisper.ModelPath = modelPath;
        App.Settings.Save();

        Task.Run(async () => await InitializeAsync());
    }

    public void Dispose()
    {
        _factory?.Dispose();
    }
}
