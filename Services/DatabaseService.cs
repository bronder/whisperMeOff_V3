using System.IO;
using Microsoft.Data.Sqlite;
using whisperMeOff.Models.Transformation;

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

            // Create tables if not exists
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

                -- Transformation profiles for user-defined transformation settings
                CREATE TABLE IF NOT EXISTS transformation_profiles (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    description TEXT,
                    transformation_type TEXT NOT NULL,
                    direction TEXT NOT NULL,
                    temperature REAL DEFAULT 0.7,
                    max_tokens INTEGER DEFAULT 4096,
                    preserve_proper_nouns INTEGER DEFAULT 1,
                    preserve_technical_terms INTEGER DEFAULT 1,
                    custom_prompt TEXT,
                    is_default INTEGER DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_profiles_type ON transformation_profiles (transformation_type);

                -- Transformation history for tracking transformations
                CREATE TABLE IF NOT EXISTS transformation_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    original_text TEXT NOT NULL,
                    transformed_text TEXT NOT NULL,
                    transformation_type TEXT NOT NULL,
                    direction TEXT NOT NULL,
                    profile_id INTEGER,
                    quality_score REAL,
                    processing_time_ms INTEGER,
                    timestamp TEXT NOT NULL,
                    transcription_id INTEGER,
                    FOREIGN KEY (profile_id) REFERENCES transformation_profiles(id) ON DELETE SET NULL,
                    FOREIGN KEY (transcription_id) REFERENCES transcriptions(id) ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS idx_history_timestamp ON transformation_history (timestamp DESC);
                CREATE INDEX IF NOT EXISTS idx_history_profile ON transformation_history (profile_id);
                CREATE INDEX IF NOT EXISTS idx_history_type ON transformation_history (transformation_type);
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

    /// <summary>
    /// Update a transcription's text
    /// </summary>
    /// <param name="id">The transcription ID</param>
    /// <param name="text">The new text</param>
    /// <returns>True if updated</returns>
    public async Task<bool> UpdateTranscriptionAsync(long id, string text)
    {
        if (_connection == null) return false;

        try
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE transcriptions SET text = $text WHERE id = $id";
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$id", id);

            var result = await cmd.ExecuteNonQueryAsync();
            return result > 0;
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Update transcription error");
            return false;
        }
    }

#region Transformation Profiles

/// <summary>
/// Adds a new transformation profile to the database.
/// </summary>
/// <param name="profile">The transformation profile to add.</param>
/// <returns>The ID of the inserted profile, or -1 on failure.</returns>
public async Task<long> AddTransformationProfileAsync(TransformationProfileRecord profile)
{
    if (_connection == null) return -1;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO transformation_profiles
            (name, description, transformation_type, direction, temperature, max_tokens,
             preserve_proper_nouns, preserve_technical_terms, custom_prompt, is_default, created_at, updated_at)
            VALUES ($name, $description, $type, $direction, $temperature, $maxTokens,
                    $preserveProperNouns, $preserveTechnicalTerms, $customPrompt, $isDefault, $createdAt, $updatedAt);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$description", profile.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$type", profile.TransformationType);
        cmd.Parameters.AddWithValue("$direction", profile.Direction);
        cmd.Parameters.AddWithValue("$temperature", profile.Temperature);
        cmd.Parameters.AddWithValue("$maxTokens", profile.MaxTokens);
        cmd.Parameters.AddWithValue("$preserveProperNouns", profile.PreserveProperNouns ? 1 : 0);
        cmd.Parameters.AddWithValue("$preserveTechnicalTerms", profile.PreserveTechnicalTerms ? 1 : 0);
        cmd.Parameters.AddWithValue("$customPrompt", profile.CustomPrompt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Add transformation profile error");
        return -1;
    }
}

/// <summary>
/// Gets all transformation profiles.
/// </summary>
/// <returns>List of transformation profiles.</returns>
public async Task<List<TransformationProfileRecord>> GetTransformationProfilesAsync()
{
    var profiles = new List<TransformationProfileRecord>();
    if (_connection == null) return profiles;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, transformation_type, direction, temperature, max_tokens,
                   preserve_proper_nouns, preserve_technical_terms, custom_prompt, is_default, created_at, updated_at
            FROM transformation_profiles ORDER BY name ASC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            profiles.Add(ReadTransformationProfile(reader));
        }
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Get transformation profiles error");
    }

    return profiles;
}

/// <summary>
/// Gets transformation profiles by type.
/// </summary>
/// <param name="type">The transformation type.</param>
/// <returns>List of matching profiles.</returns>
public async Task<List<TransformationProfileRecord>> GetTransformationProfilesByTypeAsync(string type)
{
    var profiles = new List<TransformationProfileRecord>();
    if (_connection == null) return profiles;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, transformation_type, direction, temperature, max_tokens,
                   preserve_proper_nouns, preserve_technical_terms, custom_prompt, is_default, created_at, updated_at
            FROM transformation_profiles WHERE transformation_type = $type ORDER BY name ASC";
        cmd.Parameters.AddWithValue("$type", type);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            profiles.Add(ReadTransformationProfile(reader));
        }
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Get transformation profiles by type error");
    }

    return profiles;
}

/// <summary>
/// Gets a transformation profile by ID.
/// </summary>
/// <param name="id">The profile ID.</param>
/// <returns>The profile, or null if not found.</returns>
public async Task<TransformationProfileRecord?> GetTransformationProfileAsync(long id)
{
    if (_connection == null) return null;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, transformation_type, direction, temperature, max_tokens,
                   preserve_proper_nouns, preserve_technical_terms, custom_prompt, is_default, created_at, updated_at
            FROM transformation_profiles WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadTransformationProfile(reader);
        }
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Get transformation profile error");
    }

    return null;
}

/// <summary>
/// Gets the default transformation profile.
/// </summary>
/// <returns>The default profile, or null if none is set as default.</returns>
public async Task<TransformationProfileRecord?> GetDefaultTransformationProfileAsync()
{
    if (_connection == null) return null;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, transformation_type, direction, temperature, max_tokens,
                   preserve_proper_nouns, preserve_technical_terms, custom_prompt, is_default, created_at, updated_at
            FROM transformation_profiles WHERE is_default = 1 LIMIT 1";

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadTransformationProfile(reader);
        }
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Get default transformation profile error");
    }

    return null;
}

/// <summary>
/// Updates an existing transformation profile.
/// </summary>
/// <param name="profile">The profile to update.</param>
/// <returns>True if successful.</returns>
public async Task<bool> UpdateTransformationProfileAsync(TransformationProfileRecord profile)
{
    if (_connection == null) return false;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE transformation_profiles
            SET name = $name, description = $description, transformation_type = $type,
                direction = $direction, temperature = $temperature, max_tokens = $maxTokens,
                preserve_proper_nouns = $preserveProperNouns, preserve_technical_terms = $preserveTechnicalTerms,
                custom_prompt = $customPrompt, is_default = $isDefault, updated_at = $updatedAt
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", profile.Id);
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$description", profile.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$type", profile.TransformationType);
        cmd.Parameters.AddWithValue("$direction", profile.Direction);
        cmd.Parameters.AddWithValue("$temperature", profile.Temperature);
        cmd.Parameters.AddWithValue("$maxTokens", profile.MaxTokens);
        cmd.Parameters.AddWithValue("$preserveProperNouns", profile.PreserveProperNouns ? 1 : 0);
        cmd.Parameters.AddWithValue("$preserveTechnicalTerms", profile.PreserveTechnicalTerms ? 1 : 0);
        cmd.Parameters.AddWithValue("$customPrompt", profile.CustomPrompt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Update transformation profile error");
        return false;
    }
}

/// <summary>
/// Deletes a transformation profile by ID.
/// </summary>
/// <param name="id">The profile ID to delete.</param>
/// <returns>True if successful.</returns>
public async Task<bool> DeleteTransformationProfileAsync(long id)
{
    if (_connection == null) return false;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM transformation_profiles WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Delete transformation profile error");
        return false;
    }
}

/// <summary>
/// Sets a profile as the default (clears other defaults first).
/// </summary>
/// <param name="profileId">The profile ID to set as default.</param>
/// <returns>True if successful.</returns>
public async Task<bool> SetDefaultTransformationProfileAsync(long profileId)
{
    if (_connection == null) return false;

    try
    {
        using var transaction = _connection.BeginTransaction();
        
        // Clear all defaults
        var clearCmd = _connection.CreateCommand();
        clearCmd.CommandText = "UPDATE transformation_profiles SET is_default = 0";
        await clearCmd.ExecuteNonQueryAsync();

        // Set new default
        var setCmd = _connection.CreateCommand();
        setCmd.CommandText = "UPDATE transformation_profiles SET is_default = 1, updated_at = $updatedAt WHERE id = $id";
        setCmd.Parameters.AddWithValue("$id", profileId);
        setCmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o"));
        await setCmd.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
        return true;
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Set default transformation profile error");
        return false;
    }
}

private TransformationProfileRecord ReadTransformationProfile(SqliteDataReader reader)
{
    return new TransformationProfileRecord
    {
        Id = reader.GetInt64(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        TransformationType = reader.GetString(3),
        Direction = reader.GetString(4),
        Temperature = reader.GetDouble(5),
        MaxTokens = reader.GetInt32(6),
        PreserveProperNouns = reader.GetInt32(7) == 1,
        PreserveTechnicalTerms = reader.GetInt32(8) == 1,
        CustomPrompt = reader.IsDBNull(9) ? null : reader.GetString(9),
        IsDefault = reader.GetInt32(10) == 1,
        CreatedAt = reader.GetString(11),
        UpdatedAt = reader.GetString(12)
    };
}

#endregion

#region Transformation History

/// <summary>
/// Adds a transformation history record.
/// </summary>
/// <param name="record">The history record to add.</param>
/// <returns>The ID of the inserted record, or -1 on failure.</returns>
public async Task<long> AddTransformationHistoryAsync(TransformationHistoryRecord record)
{
    if (_connection == null) return -1;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO transformation_history
            (original_text, transformed_text, transformation_type, direction, profile_id,
             quality_score, processing_time_ms, timestamp, transcription_id)
            VALUES ($originalText, $transformed_text, $type, $direction, $profile_id,
                    $quality_score, $processing_time_ms, $timestamp, $transcription_id)
        ";
        cmd.Parameters.AddWithValue("$originalText", record.OriginalText);
        cmd.Parameters.AddWithValue("$transformed_text", record.TransformedText);
        cmd.Parameters.AddWithValue("$type", record.TransformationType);
        cmd.Parameters.AddWithValue("$direction", record.Direction);
        cmd.Parameters.AddWithValue("$profile_id", record.ProfileId.HasValue ? record.ProfileId.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$quality_score", record.QualityScore.HasValue ? record.QualityScore.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$processing_time_ms", record.ProcessingTimeMs);
        cmd.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$transcription_id", record.TranscriptionId.HasValue ? record.TranscriptionId.Value : (object)DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Add transformation history error");
        return -1;
    }
}

/// <summary>
/// Gets transformation history records.
/// </summary>
/// <param name="limit">Maximum number of records to retrieve.</param>
/// <returns>List of history records.</returns>
public async Task<List<TransformationHistoryRecord>> GetTransformationHistoryAsync(int limit = 50)
{
    var records = new List<TransformationHistoryRecord>();
    if (_connection == null) return records;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, original_text, transformed_text, transformation_type, direction, profile_id,
                   quality_score, processing_time_ms, timestamp, transcription_id
            FROM transformation_history ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(ReadTransformationHistoryRecord(reader));
        }
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Get transformation history error");
    }

    return records;
}

/// <summary>
/// Gets transformation history for a specific profile.
/// </summary>
/// <param name="profileId">The profile ID.</param>
/// <param name="limit">Maximum number of records.</param>
/// <returns>List of history records.</returns>
public async Task<List<TransformationHistoryRecord>> GetTransformationHistoryByProfileAsync(long profileId, int limit = 50)
{
    var records = new List<TransformationHistoryRecord>();
    if (_connection == null) return records;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, original_text, transformed_text, transformation_type, direction, profile_id,
                   quality_score, processing_time_ms, timestamp, transcription_id
            FROM transformation_history WHERE profile_id = $profileId ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$profileId", profileId);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(ReadTransformationHistoryRecord(reader));
        }
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Get transformation history by profile error");
    }

    return records;
}

/// <summary>
/// Gets a transformation history record by ID.
/// </summary>
/// <param name="id">The record ID.</param>
/// <returns>The history record, or null if not found.</returns>
public async Task<TransformationHistoryRecord?> GetTransformationHistoryAsync(long id)
{
    if (_connection == null) return null;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, original_text, transformed_text, transformation_type, direction, profile_id,
                   quality_score, processing_time_ms, timestamp, transcription_id
            FROM transformation_history WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadTransformationHistoryRecord(reader);
        }
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Get transformation history error");
    }

    return null;
}

/// <summary>
/// Searches transformation history by original text content.
/// </summary>
/// <param name="query">The search query.</param>
/// <returns>List of matching records.</returns>
public async Task<List<TransformationHistoryRecord>> SearchTransformationHistoryAsync(string query)
{
    var records = new List<TransformationHistoryRecord>();
    if (_connection == null) return records;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, original_text, transformed_text, transformation_type, direction, profile_id,
                   quality_score, processing_time_ms, timestamp, transcription_id
            FROM transformation_history WHERE original_text LIKE $query ORDER BY timestamp DESC LIMIT 50";
        cmd.Parameters.AddWithValue("$query", $"%{query}%");

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(ReadTransformationHistoryRecord(reader));
        }
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Search transformation history error");
    }

    return records;
}

/// <summary>
/// Clears all transformation history.
/// </summary>
/// <returns>True if successful.</returns>
public async Task<bool> ClearTransformationHistoryAsync()
{
    if (_connection == null) return false;

    try
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM transformation_history";
        await cmd.ExecuteNonQueryAsync();
        return true;
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Clear transformation history error");
        return false;
    }
}

/// <summary>
/// Clears transformation history older than specified time span.
/// </summary>
/// <param name="olderThan">Delete records older than this duration.</param>
/// <returns>Number of records deleted.</returns>
public async Task<int> ClearTransformationHistoryOlderThanAsync(TimeSpan olderThan)
{
    if (_connection == null) return 0;

    try
    {
        var cutoffTime = DateTime.Now.Subtract(olderThan);
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM transformation_history WHERE timestamp < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoffTime.ToString("o"));

        var rowsDeleted = await cmd.ExecuteNonQueryAsync();
        return rowsDeleted;
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Clear older transformation history error");
        return 0;
    }
}

/// <summary>
/// Gets statistics about transformation history.
/// </summary>
/// <returns>Dictionary with statistics.</returns>
public async Task<Dictionary<string, object>> GetTransformationHistoryStatsAsync()
{
    var stats = new Dictionary<string, object>();
    if (_connection == null) return stats;

    try
    {
        // Total count
        var totalCmd = _connection.CreateCommand();
        totalCmd.CommandText = "SELECT COUNT(*) FROM transformation_history";
        stats["TotalTransformations"] = Convert.ToInt64(await totalCmd.ExecuteScalarAsync());

        // Average quality score
        var avgCmd = _connection.CreateCommand();
        avgCmd.CommandText = "SELECT AVG(quality_score) FROM transformation_history WHERE quality_score IS NOT NULL";
        var avgResult = await avgCmd.ExecuteScalarAsync();
        stats["AverageQualityScore"] = avgResult == DBNull.Value ? 0 : Convert.ToDouble(avgResult);

        // Average processing time
        var avgTimeCmd = _connection.CreateCommand();
        avgTimeCmd.CommandText = "SELECT AVG(processing_time_ms) FROM transformation_history WHERE processing_time_ms IS NOT NULL";
        var avgTimeResult = await avgTimeCmd.ExecuteScalarAsync();
        stats["AverageProcessingTimeMs"] = avgTimeResult == DBNull.Value ? 0 : Convert.ToDouble(avgTimeResult);

        // Count by type
        var byTypeCmd = _connection.CreateCommand();
        byTypeCmd.CommandText = "SELECT transformation_type, COUNT(*) FROM transformation_history GROUP BY transformation_type";
        var byTypeDict = new Dictionary<string, long>();
        using (var reader = await byTypeCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                byTypeDict[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        stats["ByType"] = byTypeDict;

        // Count by direction
        var byDirectionCmd = _connection.CreateCommand();
        byDirectionCmd.CommandText = "SELECT direction, COUNT(*) FROM transformation_history GROUP BY direction";
        var byDirectionDict = new Dictionary<string, long>();
        using (var reader = await byDirectionCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                byDirectionDict[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        stats["ByDirection"] = byDirectionDict;
    }
    catch (Exception ex)
    {
        LoggingService.Error(ex, "Get transformation history stats error");
    }

    return stats;
}

private TransformationHistoryRecord ReadTransformationHistoryRecord(SqliteDataReader reader)
{
    return new TransformationHistoryRecord
    {
        Id = reader.GetInt64(0),
        OriginalText = reader.GetString(1),
        TransformedText = reader.GetString(2),
        TransformationType = reader.GetString(3),
        Direction = reader.GetString(4),
        ProfileId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
        QualityScore = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        ProcessingTimeMs = reader.GetInt32(7),
        Timestamp = reader.GetString(8),
        TranscriptionId = reader.IsDBNull(9) ? null : reader.GetInt64(9)
    };
}

#endregion

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
