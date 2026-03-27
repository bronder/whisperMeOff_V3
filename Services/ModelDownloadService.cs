using System.IO;
using System.Net.Http;

namespace whisperMeOff.Services;

public class ModelDownloadService : IDisposable
{
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cancellationTokenSource;

    // Base paths for validation
    private static readonly string[] AllowedBasePaths = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    };

    public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;

    public ModelDownloadService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Validates that a download path is safe (within allowed directories)
    /// </summary>
    private bool IsPathSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var fullPath = Path.GetFullPath(path);

            // Check if path starts with any allowed base path
            foreach (var basePath in AllowedBasePaths)
            {
                var normalizedBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Also allow paths within the app's models directory
            if (!string.IsNullOrEmpty(App.ModelsPath))
            {
                var appBase = Path.GetFullPath(App.ModelsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var normalizedAppPath = fullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (normalizedAppPath.StartsWith(appBase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            LoggingService.Warn($"[ModelDownload] Path validation failed: {path} is not within allowed directories");
            return false;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, $"[ModelDownload] Path validation error for: {path}");
            return false;
        }
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

            // Validate the download path for Whisper models
            var whisperDownloadPath = App.Settings.General.ModelDownloadPath;
            if (!string.IsNullOrEmpty(whisperDownloadPath) && !IsPathSafe(whisperDownloadPath))
            {
                LoggingService.Error($"[ModelDownload] Unsafe Whisper download path rejected: {whisperDownloadPath}");
                throw new InvalidOperationException($"Download path is not allowed: {whisperDownloadPath}. Path must be within AppData, LocalAppData, or UserProfile directories.");
            }

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

    public async Task<string?> DownloadLlamaModelAsync(string modelId, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
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

            // Validate the download path to prevent path traversal attacks
            if (!string.IsNullOrEmpty(downloadPath) && !IsPathSafe(modelDir))
            {
                LoggingService.Error($"[ModelDownload] Unsafe download path rejected: {modelDir}");
                throw new InvalidOperationException($"Download path is not allowed: {modelDir}. Path must be within AppData, LocalAppData, or UserProfile directories.");
            }

            var destinationPath = Path.Combine(modelDir, fileName);

            // Check if already exists
            if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 1024 * 1024)
            {
                LoggingService.Debug($"Llama model already exists: {destinationPath}");
                return destinationPath;
            }

            // Create internal cancellation token and link with external token
            _cancellationTokenSource = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);

            // Add auth header if token is set
            if (!string.IsNullOrEmpty(App.Settings.Llama.HuggingFaceToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", App.Settings.Llama.HuggingFaceToken);
            }

            return await DownloadFileAsync(fileUrl, destinationPath, progress, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            LoggingService.Info("[ModelDownload] Download cancelled by user");
            return "ERROR:Download cancelled";
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Llama model download failed");
            return null;
        }
    }

    private async Task<string?> DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
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
        catch (OperationCanceledException)
        {
            // Clean up partial file on cancellation
            if (File.Exists(destinationPath))
            {
                LoggingService.Info($"[ModelDownload] Cleaning up partial file: {destinationPath}");
                try
                {
                    File.Delete(destinationPath);
                }
                catch (Exception ex)
                {
                    LoggingService.Warn($"[ModelDownload] Failed to delete partial file: {ex.Message}");
                }
            }
            throw; // Re-throw to let caller handle
        }
    }

    /// <summary>
    /// Searches HuggingFace for GGUF variants of a model when direct lookup fails.
    /// </summary>
    private async Task<string?> SearchGgufVariantsAsync(string ownerRepo)
    {
        try
        {
            // Extract the model name from owner/repo format (e.g., "google/gemma-2b" -> "gemma-2b")
            var modelName = ownerRepo.Contains("/") ? ownerRepo.Split('/').Last() : ownerRepo;
            
            // Remove common suffixes that might interfere with search
            modelName = modelName.Replace("-dev", "").Replace("-base", "").Replace("-it", "");
            
            LoggingService.Info($"[ModelDownload] Searching for GGUF variants of: {modelName}");
            
            // Search for GGUF models on HuggingFace
            var searchQuery = Uri.EscapeDataString($"{modelName} GGUF");
            var searchUrl = $"https://huggingface.co/api/models?search={searchQuery}&filter=gguf&sort=downloads&direction=-1&limit=5";
            
            _httpClient.DefaultRequestHeaders.Authorization = null; // Public search
            
            using var response = await _httpClient.GetAsync(searchUrl);
            if (!response.IsSuccessStatusCode)
            {
                LoggingService.Debug($"[ModelDownload] Search API returned: {response.StatusCode}");
                return null;
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var searchResults = System.Text.Json.JsonSerializer.Deserialize<List<HuggingFaceModelInfo>>(json);
            
            if (searchResults == null || searchResults.Count == 0)
            {
                LoggingService.Debug("[ModelDownload] No GGUF variants found via search");
                return null;
            }
            
            // Log the results for debugging
            LoggingService.Info($"[ModelDownload] Found {searchResults.Count} potential GGUF variants:");
            foreach (var result in searchResults.Take(3))
            {
                LoggingService.Info($"[ModelDownload]   - {result.Id} (downloads: {result.Downloads})");
            }
            
            // Try each found model to find one with GGUF files
            foreach (var model in searchResults.Take(5))
            {
                LoggingService.Debug($"[ModelDownload] Checking GGUF files in: {model.Id}");
                
                // Check if this model has GGUF files
                var ggufResult = await DiscoverGgufFilesInRepoAsync(model.Id);
                if (!string.IsNullOrEmpty(ggufResult) && !ggufResult.StartsWith("ERROR:"))
                {
                    LoggingService.Info($"[ModelDownload] Found GGUF file via search: {ggufResult}");
                    return ggufResult;
                }
            }
            
            // If we found models but none had GGUF files, suggest the best match
            var bestMatch = searchResults.First();
            return $"ERROR:No GGUF files found in '{ownerRepo}'.\n\nDid you mean: {bestMatch.Id}?\nThis model has {bestMatch.Downloads:N0} downloads and may have GGUF variants.";
        }
        catch (Exception ex)
        {
            LoggingService.Debug($"[ModelDownload] Search for GGUF variants failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks a specific repository for GGUF files and returns the download URL if found.
    /// </summary>
    private async Task<string?> DiscoverGgufFilesInRepoAsync(string ownerRepo)
    {
        try
        {
            string[] branches = { "main", "master" };
            string? foundBranch = null;
            List<HuggingFaceFile>? files = null;
            
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
                return null;
            }
            
            // Find GGUF files
            var ggufFiles = files.Where(f => 
                !string.IsNullOrEmpty(f.Path) && 
                f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (ggufFiles.Count == 0)
            {
                return null;
            }
            
            // Select the best GGUF file (prefer Q4_K_M or Q5_K_S)
            var selectedFile = ggufFiles.FirstOrDefault(f => f.Path.Contains("Q4_K_M", StringComparison.OrdinalIgnoreCase))
                       ?? ggufFiles.FirstOrDefault(f => f.Path.Contains("Q5_K_S", StringComparison.OrdinalIgnoreCase))
                       ?? ggufFiles.FirstOrDefault(f => f.Path.Contains("Q5_K_M", StringComparison.OrdinalIgnoreCase))
                       ?? ggufFiles.First();
            
            return $"https://huggingface.co/{ownerRepo}/resolve/{foundBranch}/{selectedFile.Path}";
        }
        catch (Exception ex)
        {
            LoggingService.Debug($"[ModelDownload] Error checking repo {ownerRepo}: {ex.Message}");
            return null;
        }
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
                // Try to search for GGUF variants on HuggingFace
                var searchResult = await SearchGgufVariantsAsync(ownerRepo);
                if (!string.IsNullOrEmpty(searchResult))
                {
                    return searchResult;
                }
                
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

public class HuggingFaceModelInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("downloads")]
    public long Downloads { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("likes")]
    public long Likes { get; set; }
}
