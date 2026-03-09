using System.IO;

namespace whisperMeOff.Services;

public class LlamaService : IDisposable
{
    private bool _isLoaded;
    private string? _modelPath;

    public bool IsLoaded => _isLoaded;
    public string? ModelPath => _modelPath;

    public async Task InitializeAsync(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            _isLoaded = false;
            return;
        }

        try
        {
            _modelPath = modelPath;
            // Llama model loading would happen here
            // For now, just mark as loaded
            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"Llama initialized with model: {modelPath}");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Llama initialization error: {ex.Message}");
            _isLoaded = false;
        }
    }

    public async Task<string> FormatTextAsync(string rawText)
    {
        if (!_isLoaded)
        {
            return rawText;
        }

        try
        {
            // Build prompt as per PRD
            var prompt = $@"Convert any file paths in this text to proper format. Output only the result, nothing else.

Text: {rawText}

Result:";

            // Llama inference would happen here
            // For now, return the raw text as fallback
            await Task.Delay(100); // Placeholder

            return rawText;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Llama formatting error: {ex.Message}");
            // Fallback to raw text as per PRD
            return rawText;
        }
    }

    public void Unload()
    {
        _isLoaded = false;
        _modelPath = null;
    }

    public void Dispose()
    {
        Unload();
    }
}
