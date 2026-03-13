using System.IO;
using Microsoft.Data.Sqlite;

namespace whisperMeOff.Services;

/// <summary>
/// Service for storing and retrieving transcription history using SQLite.
/// Provides async CRUD operations for transcription records.
/// </summary>
/// <remarks>
/// Database is stored at %APPDATA%/whisperMeOff/transcriptions.db.
/// Schema includes timestamp indexing for efficient history queries.
/// </remarks>
public class DatabaseService : IDisposable
{
    private SqliteConnection? _connection;

    /// <summary>
    /// Initializes the database connection and creates tables if needed.
    /// </summary>
    /// <remarks>
    /// Creates the transcriptions table with an index on timestamp for fast queries.
    /// </remarks>
    public async Task InitializeAsync()
    {
        try
        {
            _connection = new SqliteConnection($"Data Source={App.DatabasePath}");
            await _connection.OpenAsync();

            // Create table if not exists
            var createTableCmd = _connection.CreateCommand();
            createTableCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS transcriptions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    text TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    duration REAL,
                    model TEXT,
                    language TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_transcriptions_timestamp ON transcriptions (timestamp DESC);
            ";
            await createTableCmd.ExecuteNonQueryAsync();

            LoggingService.Info("Database initialized");
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Database initialization error");
        }
    }

    /// <summary>
    /// Adds a new transcription record to the database.
    /// </summary>
    /// <param name="text">The transcribed text.</param>
    /// <param name="duration">Recording duration in seconds.</param>
    /// <param name="model">The Whisper model used.</param>
    /// <param name="language">The detected or selected language code.</param>
    /// <returns>The ID of the inserted record, or -1 on failure.</returns>
    public async Task<long> AddTranscriptionAsync(string text, double duration, string? model, string? language)
    {
        if (_connection == null) return -1;

        try
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO transcriptions (text, timestamp, duration, model, language)
                VALUES ($text, $timestamp, $duration, $model, $language);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$duration", duration);
            cmd.Parameters.AddWithValue("$model", model ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$language", language ?? (object)DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Add transcription error");
            return -1;
        }
    }

    /// <summary>
    /// Gets the most recent transcriptions.
    /// </summary>
    /// <param name="limit">Maximum number of records to retrieve (default 50).</param>
    /// <returns>List of transcription records ordered by timestamp descending.</returns>
    public async Task<List<TranscriptionRecord>> GetTranscriptionsAsync(int limit = 50)
    {
        var records = new List<TranscriptionRecord>();
        if (_connection == null) return records;

        try
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, text, timestamp, duration, model, language FROM transcriptions ORDER BY timestamp DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                records.Add(new TranscriptionRecord
                {
                    Id = reader.GetInt64(0),
                    Text = reader.GetString(1),
                    Timestamp = reader.GetString(2),
                    Duration = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    Model = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Language = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Get transcriptions error");
        }

        return records;
    }

    /// <summary>
    /// Gets a single transcription by ID.
    /// </summary>
    /// <param name="id">The transcription ID.</param>
    /// <returns>The transcription record, or null if not found.</returns>
    public async Task<TranscriptionRecord?> GetTranscriptionAsync(long id)
    {
        if (_connection == null) return null;

        try
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, text, timestamp, duration, model, language FROM transcriptions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TranscriptionRecord
                {
                    Id = reader.GetInt64(0),
                    Text = reader.GetString(1),
                    Timestamp = reader.GetString(2),
                    Duration = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    Model = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Language = reader.IsDBNull(5) ? null : reader.GetString(5)
                };
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Get transcription error");
        }

        return null;
    }

    /// <summary>
    /// Deletes a transcription by ID.
    /// </summary>
    /// <param name="id">The transcription ID to delete.</param>
    /// <returns>True if deletion was successful.</returns>
    public async Task<bool> DeleteTranscriptionAsync(long id)
    {
        if (_connection == null) return false;

        try
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM transcriptions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            var result = await cmd.ExecuteNonQueryAsync();
            return result > 0;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Delete transcription error");
            return false;
        }
    }

    /// <summary>
    /// Clears all transcription history.
    /// </summary>
    /// <returns>True if operation was successful.</returns>
    public async Task<bool> ClearAllTranscriptionsAsync()
    {
        if (_connection == null) return false;

        try
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM transcriptions";

            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Clear transcriptions error");
            return false;
        }
    }

    /// <summary>
    /// Deletes transcriptions older than the specified time span.
    /// </summary>
    /// <param name="olderThan">Delete records older than this duration.</param>
    /// <returns>Number of records deleted.</returns>
    public async Task<int> ClearTranscriptionsOlderThanAsync(TimeSpan olderThan)
    {
        if (_connection == null) return 0;

        try
        {
            var cutoffTime = DateTime.Now.Subtract(olderThan);
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM transcriptions WHERE timestamp < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoffTime.ToString("o"));

            var rowsDeleted = await cmd.ExecuteNonQueryAsync();
            return rowsDeleted;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Clear older transcriptions error");
            return 0;
        }
    }

    /// <summary>
    /// Searches transcriptions by text content.
    /// </summary>
    /// <param name="query">The search query string.</param>
    /// <returns>List of matching transcription records.</returns>
    /// <remarks>
    /// Uses SQL LIKE for substring matching.
    /// </remarks>
    public async Task<List<TranscriptionRecord>> SearchTranscriptionsAsync(string query)
    {
        var records = new List<TranscriptionRecord>();
        if (_connection == null) return records;

        try
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, text, timestamp, duration, model, language FROM transcriptions WHERE text LIKE $query ORDER BY timestamp DESC LIMIT 50";
            cmd.Parameters.AddWithValue("$query", $"%{query}%");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                records.Add(new TranscriptionRecord
                {
                    Id = reader.GetInt64(0),
                    Text = reader.GetString(1),
                    Timestamp = reader.GetString(2),
                    Duration = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    Model = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Language = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Search transcriptions error");
        }

        return records;
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}

/// <summary>
/// Represents a single transcription record from the database.
/// </summary>
public class TranscriptionRecord
{
    /// <summary>The unique record ID.</summary>
    public long Id { get; set; }

    /// <summary>The transcribed text content.</summary>
    public string Text { get; set; } = "";

    /// <summary>ISO 8601 timestamp of the transcription.</summary>
    public string Timestamp { get; set; } = "";

    /// <summary>Recording duration in seconds.</summary>
    public double? Duration { get; set; }

    /// <summary>The Whisper model used for transcription.</summary>
    public string? Model { get; set; }

    /// <summary>The language code used.</summary>
    public string? Language { get; set; }
}
