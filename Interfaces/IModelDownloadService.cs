namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for downloading Whisper and LLama models
/// </summary>
public interface IModelDownloadService : IDisposable
{
    /// <summary>
    /// Raised when download progress changes
    /// </summary>
    event EventHandler<DownloadProgressEventArgs>? DownloadProgress;

    /// <summary>
    /// Download Whisper model
    /// </summary>
    /// <param name="size">Model size (tiny, base, small, medium, large)</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>Path to downloaded model or null on failure</returns>
    Task<string?> DownloadWhisperModelAsync(string size, IProgress<double>? progress = null);

    /// <summary>
    /// Download LLama model
    /// </summary>
    /// <param name="modelId">Model identifier from HuggingFace</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>Path to downloaded model or null on failure</returns>
    Task<string?> DownloadLlamaModelAsync(string modelId, IProgress<double>? progress = null);

    /// <summary>
    /// Cancel ongoing download
    /// </summary>
    void CancelDownload();
}

/// <summary>
/// Event args for download progress
/// </summary>
public class DownloadProgressEventArgs : EventArgs
{
    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public double ProgressPercentage { get; }

    /// <summary>
    /// Bytes downloaded so far
    /// </summary>
    public long BytesDownloaded { get; }

    /// <summary>
    /// Total bytes to download
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// Current download speed in bytes/second
    /// </summary>
    public double SpeedBytesPerSecond { get; }

    public DownloadProgressEventArgs(double progressPercentage, long bytesDownloaded, long totalBytes, double speedBytesPerSecond)
    {
        ProgressPercentage = progressPercentage;
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
        SpeedBytesPerSecond = speedBytesPerSecond;
    }
}
