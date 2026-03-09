using System.IO;
using LLama;
using LLama.Common;

namespace whisperMeOff.Services;

public class LlamaService : IDisposable
{
    private bool _isLoaded;
    private string? _modelPath;
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;

    public bool IsLoaded => _isLoaded;
    public string? ModelPath => _modelPath;

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
            
            // Load model parameters
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = 512,
                GpuLayerCount = 0, // Use CPU by default
            };
            
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Loading model from: {modelPath}");
            
            // Load the model synchronously
            _model = LLamaWeights.LoadFromFile(parameters);
            _context = _model.CreateContext(parameters);
            _executor = new InteractiveExecutor(_context);
            
            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Successfully initialized with model: {modelPath}");
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
                    $"Failed to load Llama model: {ex.Message}\n\nMake sure you have a valid GGUF model file (not a vision model).",
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
            
            // Clean up/format the transcription
            var prompt = $@"Fix the capitalization and punctuation in this text. Make it readable. Return only the corrected text, nothing else.

Text: {rawText}

Corrected:";

            System.Diagnostics.Debug.WriteLine("[LLAMA] === FULL PROMPT ===");
            System.Diagnostics.Debug.WriteLine(prompt);
            System.Diagnostics.Debug.WriteLine("[LLAMA] === END PROMPT ===");

            var inferenceParams = new InferenceParams
            {
                Temperature = 0.1f,
                MaxTokens = 512,
                AntiPrompts = new List<string> { "Text:", "Corrected:", "\n\n", "Here is", "Sure,", "Here'", "Explanation:" }
            };

            System.Diagnostics.Debug.WriteLine("[LLAMA] Starting inference...");
            var result = new System.Text.StringBuilder();
            
            await foreach (var token in _executor.InferAsync(prompt, inferenceParams))
            {
                result.Append(token);
                
                // Stop if we hit a reasonable length
                if (result.Length > rawText.Length * 3)
                    break;
            }

            var formatted = result.ToString().Trim();
            
            // Log the full output for debugging
            System.Diagnostics.Debug.WriteLine($"[LLAMA] Raw output: {formatted}");
            
            // Clean up any common artifacts from the model
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
            if (trimmed.StartsWith("Text:") || trimmed.StartsWith("Corrected:") || 
                trimmed.StartsWith("Input") || trimmed.StartsWith("Output") ||
                trimmed.StartsWith("Here is") || trimmed.StartsWith("Sure,") ||
                trimmed.StartsWith("Here'") || trimmed.StartsWith("The formatted") ||
                trimmed.StartsWith("Here's") || trimmed.StartsWith("Human:") ||
                trimmed.StartsWith("AI:") || trimmed.StartsWith("Assistant:") ||
                trimmed.StartsWith("Can you") || trimmed.StartsWith("Please"))
            {
                continue;
            }
            
            cleanedLines.Add(line);
        }
        
        var result = string.Join("\n", cleanedLines).Trim();
        
        // If the output is way too long, it probably includes garbage
        if (result.Length > original.Length * 4)
        {
            // Try to find where the actual content starts
            var idx = result.IndexOf(original, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                result = result.Substring(0, idx).Trim();
            }
        }
        
        // If the output contains the exact original text, it's likely garbage - remove it
        if (!string.IsNullOrEmpty(original) && result.Contains(original))
        {
            result = result.Replace(original, "").Trim();
        }
        
        // Remove anything after common prompt patterns that appear in the middle of output
        var patternsToRemove = new[] { "Human:", "AI:", "Assistant:", "Can you", "Please provide", "INPUT:", "OUTPUT:" };
        foreach (var pattern in patternsToRemove)
        {
            var idx = result.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx > 0 && idx < result.Length - pattern.Length - 10) // Not at the very beginning, leave some content
            {
                result = result.Substring(0, idx).Trim();
            }
        }
        
        return result;
    }

    public void Unload()
    {
        _isLoaded = false;
        _modelPath = null;
        
        _executor = null;
        _context?.Dispose();
        _context = null;
        _model?.Dispose();
        _model = null;
    }

    public void Dispose()
    {
        Unload();
    }
}
