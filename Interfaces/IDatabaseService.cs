namespace whisperMeOff.Interfaces;

/// <summary>
/// Service for SQLite database operations (transcription history)
/// </summary>
public interface IDatabaseService : IDisposable
{
    /// <summary>
    /// Initialize the database
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Add a new transcription record
    /// </summary>
    /// <returns>ID of the inserted record</returns>
    Task<long> AddTranscriptionAsync(string text, double duration, string? model, string? language);

    /// <summary>
    /// Get recent transcriptions
    /// </summary>
    /// <param name="limit">Maximum number of records</param>
    Task<List<TranscriptionRecord>> GetTranscriptionsAsync(int limit = 50);

    /// <summary>
    /// Get a specific transcription by ID
    /// </summary>
    Task<TranscriptionRecord?> GetTranscriptionAsync(long id);

    /// <summary>
    /// Delete a transcription
    /// </summary>
    /// <returns>True if deleted</returns>
    Task<bool> DeleteTranscriptionAsync(long id);

    /// <summary>
    /// Clear all transcriptions
    /// </summary>
    /// <returns>True if cleared</returns>
    Task<bool> ClearAllTranscriptionsAsync();

    /// <summary>
    /// Delete transcriptions older than specified time
    /// </summary>
    /// <returns>Number of records deleted</returns>
    Task<int> ClearTranscriptionsOlderThanAsync(TimeSpan olderThan);

    /// <summary>
    /// Search transcriptions by text
    /// </summary>
    Task<List<TranscriptionRecord>> SearchTranscriptionsAsync(string query);
}

/// <summary>
/// Transcription record from database
/// </summary>
public class TranscriptionRecord
{
    /// <summary>
    /// Unique ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Transcribed text
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp in ISO format
    /// </summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>
    /// Duration in seconds
    /// </summary>
    public double? Duration { get; set; }

    /// <summary>
    /// Model used for transcription
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Language detected/used
    /// </summary>
    public string? Language { get; set; }
}
