using System.IO;
using System.Net.Http;

namespace whisperMeOff.Services;

public class ModelDownloadService : IDisposable
{
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;

    public ModelDownloadService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(30);
    }

    public async Task<string?> DownloadWhisperModelAsync(string size, IProgress<double>? progress = null)
    {
        try
        {
            var url = GetWhisperDownloadUrl(size);
            
            // Handle large model filename (v3 naming)
            var fileName = size.ToLowerInvariant() == "large" 
                ? "ggml-large-v3.bin" 
                : $"ggml-{size}.bin";
            
            var destinationPath = Path.Combine(App.WhisperModelsPath, fileName);

            // Check if already exists (also check old large filename for backwards compatibility)
            if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 1024 * 1024)
            {
                LoggingService.Debug($"Whisper model already exists: {destinationPath}");
                return destinationPath;
            }

            // Also check for old large model filename
            if (size.ToLowerInvariant() == "large")
            {
                var oldPath = Path.Combine(App.WhisperModelsPath, "ggml-large.bin");
                if (File.Exists(oldPath) && new FileInfo(oldPath).Length > 1024 * 1024)
                {
                    LoggingService.Debug($"Model already exists (old filename): {oldPath}");
                    return oldPath;
                }
            }

            _cancellationTokenSource = new CancellationTokenSource();
            return await DownloadFileAsync(url, destinationPath, progress, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Whisper model download failed");
            return null;
        }
    }

    public async Task<string?> DownloadLlamaModelAsync(string modelId, IProgress<double>? progress = null)
    {
        try
        {
            // Discover the actual file URL from HuggingFace
            var fileUrl = await DiscoverLlamaFileUrlAsync(modelId);
            if (string.IsNullOrEmpty(fileUrl))
            {
                return null;
            }

            var fileName = Path.GetFileName(fileUrl);
            
            // Use custom Llama download path if set, otherwise use default
            var downloadPath = App.Settings.General.LlamaDownloadPath;
            var modelDir = !string.IsNullOrEmpty(downloadPath) ? downloadPath : App.LlamaModelsPath;
            var destinationPath = Path.Combine(modelDir, fileName);

            // Check if already exists
            if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 1024 * 1024)
            {
                LoggingService.Debug($"Llama model already exists: {destinationPath}");
                return destinationPath;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            // Add auth header if token is set
            if (!string.IsNullOrEmpty(App.Settings.Llama.HuggingFaceToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", App.Settings.Llama.HuggingFaceToken);
            }

            return await DownloadFileAsync(fileUrl, destinationPath, progress, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Llama model download failed");
            return null;
        }
    }

    private async Task<string?> DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                var progressValue = (double)downloadedBytes / totalBytes * 100;
                progress?.Report(progressValue);
                DownloadProgress?.Invoke(this, new DownloadProgressEventArgs(progressValue, downloadedBytes, totalBytes));
            }
        }

        // Validate file size
        if (totalBytes > 0 && downloadedBytes < 1024 * 1024)
        {
            // File too small, probably error page
            File.Delete(destinationPath);
            throw new Exception("Downloaded file too small - likely an error page");
        }

        return destinationPath;
    }

    private async Task<string?> DiscoverLlamaFileUrlAsync(string modelId)
    {
        try
        {
            // Parse model ID and quantization
            string ownerRepo;
            string? quantization = null;

            if (modelId.Contains(":"))
            {
                var parts = modelId.Split(':');
                ownerRepo = parts[0];
                quantization = parts[1];
            }
            else
            {
                ownerRepo = modelId;
            }

            // Try both main and master branches
            string[] branches = { "main", "master" };
            string? foundBranch = null;
            List<HuggingFaceFile>? files = null;

            if (!string.IsNullOrEmpty(App.Settings.Llama.HuggingFaceToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", App.Settings.Llama.HuggingFaceToken);
            }

            foreach (var branch in branches)
            {
                var apiUrl = $"https://huggingface.co/api/models/{ownerRepo}/tree/{branch}";
                
                using var response = await _httpClient.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    files = System.Text.Json.JsonSerializer.Deserialize<List<HuggingFaceFile>>(json);
                    if (files != null && files.Count > 0)
                    {
                        foundBranch = branch;
                        break;
                    }
                }
            }

            if (files == null || foundBranch == null)
            {
                // Try without auth for public models
                _httpClient.DefaultRequestHeaders.Authorization = null;
                foreach (var branch in branches)
                {
                    var apiUrl = $"https://huggingface.co/api/models/{ownerRepo}/tree/{branch}";
                    
                    using var response = await _httpClient.GetAsync(apiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        files = System.Text.Json.JsonSerializer.Deserialize<List<HuggingFaceFile>>(json);
                        if (files != null && files.Count > 0)
                        {
                            foundBranch = branch;
                            break;
                        }
                    }
                }
            }

            if (files == null || foundBranch == null)
            {
                return $"ERROR:Could not access model repository. Please check the model ID.";
            }

            // Find GGUF files - check both path and path with folder prefix
            var ggufFiles = files.Where(f => 
                !string.IsNullOrEmpty(f.Path) && 
                f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (ggufFiles.Count == 0) 
            {
                var fileList = string.Join(", ", files.Select(f => f.Path));
                LoggingService.Warn("No GGUF files found. Available files: " + fileList);
                LoggingService.Debug($"Total files in response: {files.Count}");
                return "ERROR:No GGUF files found. Available: " + fileList;
            }

            LoggingService.Info($"Found {ggufFiles.Count} GGUF files:");
            foreach (var f in ggufFiles)
            {
                LoggingService.Debug($"  - {f.Path} ({f.Size} bytes)");
            }

            // If quantization specified, try to match it
            HuggingFaceFile? ggufFile = null;
            if (!string.IsNullOrEmpty(quantization))
            {
                ggufFile = ggufFiles.FirstOrDefault(f => f.Path.Contains(quantization, StringComparison.OrdinalIgnoreCase));
            }

            // If no quantization match or no quantization specified, prefer Q4_K_M or Q5_K_S, then fall back to any GGUF
            if (ggufFile == null)
            {
                ggufFile = ggufFiles.FirstOrDefault(f => f.Path.Contains("Q4_K_M", StringComparison.OrdinalIgnoreCase))
                           ?? ggufFiles.FirstOrDefault(f => f.Path.Contains("Q5_K_S", StringComparison.OrdinalIgnoreCase))
                           ?? ggufFiles.FirstOrDefault(f => f.Path.Contains("Q5_K_M", StringComparison.OrdinalIgnoreCase))
                           ?? ggufFiles.FirstOrDefault();
            }

            if (ggufFile == null)
            {
                return "ERROR:No suitable GGUF file found";
            }

            LoggingService.Debug($"Selected GGUF file: {ggufFile.Path}");
            return $"https://huggingface.co/{ownerRepo}/resolve/{foundBranch}/{ggufFile.Path}";
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Discover Llama URL error");
            return $"ERROR:{ex.Message}";
        }
    }

    private string GetWhisperDownloadUrl(string size)
    {
        return size.ToLowerInvariant() switch
        {
            "tiny" => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
            "base" => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            "small" => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
            "medium" => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
            "large" => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
            _ => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{size}.bin"
        };
    }

    public void CancelDownload()
    {
        _cancellationTokenSource?.Cancel();
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Dispose();
        _httpClient.Dispose();
    }
}

public class DownloadProgressEventArgs : EventArgs
{
    public double Progress { get; }
    public long DownloadedBytes { get; }
    public long TotalBytes { get; }

    public DownloadProgressEventArgs(double progress, long downloadedBytes, long totalBytes)
    {
        Progress = progress;
        DownloadedBytes = downloadedBytes;
        TotalBytes = totalBytes;
    }
}

public class HuggingFaceFile
{
    [System.Text.Json.Serialization.JsonPropertyName("path")]
    public string Path { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("size")]
    public long Size { get; set; }
}
