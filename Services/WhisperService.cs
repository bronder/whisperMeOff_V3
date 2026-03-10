using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace whisperMeOff.Services;

public class WhisperService : IDisposable
{
    private bool _isInitialized;
    private string? _modelPath;
    private WhisperFactory? _factory;

    public bool IsInitialized => _isInitialized;

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
                System.Diagnostics.Debug.WriteLine("[Whisper] ========== MODEL LOADING ==========");
                System.Diagnostics.Debug.WriteLine($"[Whisper] Model file: {Path.GetFullPath(_modelPath)}");
                System.Diagnostics.Debug.WriteLine($"[Whisper] Model size: {new FileInfo(_modelPath).Length / 1024 / 1024} MB");
                System.Diagnostics.Debug.WriteLine("[Whisper] Runtime order: Vulkan -> CUDA -> CPU");
                
                // Add logger to capture Whisper.net library loading messages
                using var whisperLogger = Whisper.net.Logger.LogProvider.AddLogger((level, message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Whisper Lib] {level}: {message}");
                });

                _factory = WhisperFactory.FromPath(modelPath);
                System.Diagnostics.Debug.WriteLine("[Whisper] Factory loaded successfully");
                
                _isInitialized = true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Whisper model not found");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Whisper initialization error: {ex.Message}");
            _isInitialized = false;
        }
    }

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

            System.Diagnostics.Debug.WriteLine($"Starting transcription of: {audioPath}");

            // Get language setting
            var language = App.Settings.Whisper.Language;
            
            // Get custom vocabulary if set
            var customVocabulary = App.Settings.Whisper.CustomVocabulary;
            
            // Build processor with options
            var builder = _factory.CreateBuilder()
                .WithLanguage(language);

            // Apply custom vocabulary as initial prompt if set
            // Using dynamic to support different Whisper.net versions
            if (!string.IsNullOrWhiteSpace(customVocabulary))
            {
                try
                {
                    dynamic dynamicBuilder = builder;
                    dynamicBuilder = dynamicBuilder.WithInitialPrompt(customVocabulary);
                    builder = dynamicBuilder;
                    System.Diagnostics.Debug.WriteLine($"[Whisper] Using custom vocabulary: {customVocabulary.Substring(0, Math.Min(50, customVocabulary.Length))}...");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Whisper] Custom vocabulary not supported in this version: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"[Whisper] Applied {replacements.Count} word replacements");
            }
            
            System.Diagnostics.Debug.WriteLine($"Transcription complete: {transcription.Substring(0, Math.Min(50, transcription.Length))}...");
            return transcription;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Transcription error: {ex.Message}");
            throw;
        }
    }

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
