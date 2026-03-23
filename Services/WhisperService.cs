using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
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
    private readonly SemaphoreSlim _transcriptionLock = new(1, 1);
    private int _transcriptionCount;
    private const int MaxTranscriptionsBeforeReload = 100;
    private readonly object _reinitLock = new();

    /// <summary>
    /// Gets whether the Whisper service has been initialized with a model.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the name of the currently loaded model (e.g., "tiny", "small", "medium", "large").
    /// </summary>
    public string? LoadedModelName { get; private set; }

    /// <summary>
    /// Event fired when the Whisper model is loaded or unloaded.
    /// </summary>
    /// <param name="isLoaded">True if model is now loaded, false if unloaded.</param>
    public event EventHandler<bool>? ModelLoaded;

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
                // Fallback: try to find any available model, checking from large to tiny
                var modelSizes = new[] { "large-v3", "medium", "small", "base", "tiny" };
                foreach (var size in modelSizes)
                {
                    var fileName = size == "large-v3" ? "ggml-large-v3.bin" : $"ggml-{size}.bin";
                    var fallbackPath = Path.Combine(App.WhisperModelsPath, fileName);
                    if (File.Exists(fallbackPath))
                    {
                        modelPath = fallbackPath;
                        break;
                    }
                }
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
                
                // Extract model name from file path (e.g., "ggml-medium.bin" -> "medium")
                var fileName = Path.GetFileName(modelPath);
                if (fileName.StartsWith("ggml-"))
                {
                    var namePart = fileName.Substring(5); // Remove "ggml-" prefix
                    LoadedModelName = namePart.Replace(".bin", "").Replace("-v3", "");
                }
                
                _isInitialized = true;
                ModelLoaded?.Invoke(this, true);
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
    /// Checks custom path from settings first, then falls back to default location based on ModelSize setting.
    /// </remarks>
    public string GetModelPath()
    {
        if (!string.IsNullOrEmpty(App.Settings.Whisper.ModelPath) && File.Exists(App.Settings.Whisper.ModelPath))
        {
            return App.Settings.Whisper.ModelPath;
        }

        // Use ModelSize setting to determine which model file to load
        var modelSize = App.Settings.Whisper.ModelSize;
        if (string.IsNullOrEmpty(modelSize))
        {
            modelSize = "small"; // Default to small if not set
        }

        // Handle large model special case (uses ggml-large-v3.bin)
        var modelFileName = modelSize.ToLowerInvariant() == "large" 
            ? "ggml-large-v3.bin" 
            : $"ggml-{modelSize}.bin";

        // Check default locations based on ModelSize setting
        var defaultPath = Path.Combine(App.WhisperModelsPath, modelFileName);
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
    public async Task<string> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        // Validate input parameter
        if (string.IsNullOrWhiteSpace(audioPath))
        {
            throw new ArgumentException("Audio path is required", nameof(audioPath));
        }

        // Wait for any ongoing transcription to complete (Whisper factory is not thread-safe)
        await _transcriptionLock.WaitAsync(cancellationToken);
        
        try
        {
            // Check if reinitialization is needed due to many transcriptions
            if (_transcriptionCount >= MaxTranscriptionsBeforeReload && _isInitialized)
            {
                LoggingService.Info("[Whisper] Reloading factory to prevent resource exhaustion");
                ReinitializeFactory();
            }

            if (!_isInitialized || _factory == null)
            {
                throw new InvalidOperationException("Whisper not initialized");
            }
            var modelPath = GetModelPath();
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                throw new FileNotFoundException("Whisper model not found");
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException($"Audio file not found: {audioPath}");
            }

            // Validate file size to prevent DoS via huge files
            var fileInfo = new FileInfo(audioPath);
            const long MaxFileSize = 500 * 1024 * 1024; // 500MB
            if (fileInfo.Length > MaxFileSize)
            {
                throw new InvalidOperationException($"Audio file too large: {fileInfo.Length / (1024 * 1024)} MB. Maximum allowed size is {MaxFileSize / (1024 * 1024)} MB.");
            }

            if (fileInfo.Length == 0)
            {
                throw new InvalidOperationException("Audio file is empty");
            }

            // Check for cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();

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
            await foreach (var r in processor.ProcessAsync(fileStream, cancellationToken))
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
        catch (SEHException ex)
        {
            // SEHException indicates native library corruption - attempt to recover
            LoggingService.Error(ex, "Native Whisper library error - attempting recovery");
            ReinitializeFactory();
            throw new InvalidOperationException("Whisper transcription failed due to native library error. Please try again.", ex);
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Transcription error");
            throw;
        }
        finally
        {
            Interlocked.Increment(ref _transcriptionCount);
            _transcriptionLock.Release();
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

    /// <summary>
    /// Reinitializes the Whisper factory to prevent resource exhaustion in the native library.
    /// </summary>
    private void ReinitializeFactory()
    {
        lock (_reinitLock)
        {
            if (_transcriptionCount < MaxTranscriptionsBeforeReload)
            {
                return; // Already reinitialized by another thread
            }

            try
            {
                LoggingService.Info("[Whisper] Reinitializing factory to prevent native resource exhaustion");
                _factory?.Dispose();
                _factory = null;
                _isInitialized = false;
                _transcriptionCount = 0;

                // Reload synchronously
                var modelPath = GetModelPath();
                if (!string.IsNullOrEmpty(modelPath) && File.Exists(modelPath))
                {
                    _modelPath = modelPath;
                    RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cuda, RuntimeLibrary.Cuda12, RuntimeLibrary.Cpu];
                    
                    // Recreate logger (previous one was disposed with factory)
                    using var whisperLogger = Whisper.net.Logger.LogProvider.AddLogger((level, message) =>
                    {
                        LoggingService.Debug($"[Whisper Lib] {level}: {message}");
                    });

                    _factory = WhisperFactory.FromPath(modelPath);
                    _isInitialized = true;
                    LoggingService.Info("[Whisper] Factory reinitialized successfully");
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "[Whisper] Failed to reinitialize factory");
            }
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _transcriptionLock?.Dispose();
    }
}
