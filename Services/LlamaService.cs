using System.IO;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace whisperMeOff.Services;

public class LlamaService : IDisposable
{
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private StatelessExecutor? _executor;
    private bool _isLoaded;
    private string? _modelPath;
    private readonly object _lock = new();

    public bool IsLoaded => _isLoaded;
    public string? ModelPath => _modelPath;
    
    public event EventHandler<bool>? ModelLoaded;

    public async Task InitializeAsync(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Model file not found: {modelPath}");
            _isLoaded = false;
            return;
        }

        try
        {
            _modelPath = modelPath;
            
            // Load model parameters - use GPU (Vulkan) if available
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = 35, // Use Vulkan GPU
                UseMemorymap = true,
            };
            
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Loading model from: {modelPath}");
            
            // Load the model
            _model = await Task.Run(() => LLamaWeights.LoadFromFile(parameters));
            _context = await Task.Run(() => _model.CreateContext(parameters));
            _executor = new StatelessExecutor(_model, parameters);
            
            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Successfully initialized with model: {modelPath}");
            ModelLoaded?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Initialization error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Stack trace: {ex.StackTrace}");
            _isLoaded = false;
            Unload();
            
            // Notify user of the error
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Failed to load Llama model: {ex.Message}\n\nMake sure you have a valid GGUF model file.",
                    "Llama Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            });
        }
    }

    public async Task<string> FormatTextAsync(string rawText)
    {
        if (!_isLoaded || _executor == null)
        {
            System.Diagnostics.Debug.WriteLine("[LLAMA] Not loaded, returning raw text");
            return rawText;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Formatting text: {rawText.Length} chars");
            
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

            System.Diagnostics.Debug.WriteLine("[LLAMA] Starting inference...");
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Executor is null: {_executor == null}");
            
            // Run inference on background thread to avoid UI thread issues when minimized
            var result = await Task.Run(async () =>
            {
                var response = new System.Text.StringBuilder();
                
                try
                {
                    System.Diagnostics.Debug.WriteLine("[LLAMA] Starting background inference...");
                    
                    await foreach (var text in _executor!.InferAsync(prompt, inferenceParams))
                    {
                        System.Diagnostics.Debug.WriteLine($"[LLAMA] Got token: {text}");
                        response.Append(text);
                        
                        // Stop if we hit a reasonable length
                        if (response.Length > rawText.Length * 3)
                            break;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[LLAMA] Background inference complete, got {response.Length} chars");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LLAMA] Inference error: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[LLAMA] Stack trace: {ex.StackTrace}");
                }
                
                return response.ToString();
            });
            
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Task.Run completed, got {result.Length} chars");
            
            var formatted = result.Trim();
            
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Raw output: {formatted}");
            
            // Clean up any common artifacts
            formatted = CleanupLlamaOutput(formatted, rawText);
            
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Cleaned output: {formatted}");
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Inference complete: {formatted.Length} chars");
            
            // If result is empty or too different, fall back to raw text
            if (string.IsNullOrWhiteSpace(formatted) || formatted.Length < rawText.Length * 0.5)
            {
                System.Diagnostics.Debug.WriteLine("[LLAMA] Result too different, returning raw text");
                return rawText;
            }
            
            return formatted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Formatting error: {ex.Message}");
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
