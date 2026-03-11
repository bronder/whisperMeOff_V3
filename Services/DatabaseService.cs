using System.IO;
using Microsoft.Data.Sqlite;

namespace whisperMeOff.Services;

public class DatabaseService : IDisposable
{
    private SqliteConnection? _connection;

    public async void Initialize()
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

public class TranscriptionRecord
{
    public long Id { get; set; }
    public string Text { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public double? Duration { get; set; }
    public string? Model { get; set; }
    public string? Language { get; set; }
}
