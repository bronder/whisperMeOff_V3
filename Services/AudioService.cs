using NAudio.Wave;
using System.IO;

namespace whisperMeOff.Services;

/// <summary>
/// Service for capturing audio from microphone input using NAudio.
/// Handles recording, audio level monitoring, and WAV file output.
/// </summary>
/// <remarks>
/// Audio is recorded at 16kHz, mono, 16-bit PCM format for optimal Whisper transcription.
/// The service manages the recording lifecycle including start, stop, and cleanup.
/// </remarks>
public class AudioService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private DateTime _recordingStartTime;
    private bool _isRecording;
    private bool _disposed;
    private TaskCompletionSource<bool>? _recordingStoppedSource;
    private readonly object _lock = new();
    
    // Pre-recording buffer for capturing audio before trigger
    private WaveInEvent? _preBufferWaveIn;
    private readonly byte[] _preBuffer; // Circular buffer for pre-recording
    private int _preBufferWritePos;
    private bool _isPreBuffering;
    private const int PreBufferMs = 300; // 300ms pre-buffer
    private const int SampleRate = 16000;
    private const int BytesPerSample = 2; // 16-bit
    private readonly int _preBufferSize;

    /// <summary>
    /// Raised when recording starts.
    /// </summary>
    public event EventHandler? RecordingStarted;

    /// <summary>
    /// Raised when recording stops.
    /// </summary>
    public event EventHandler? RecordingStopped;

    /// <summary>
    /// Raised when audio level changes during recording. Value ranges from 0.0 to 1.0.
    /// </summary>
    /// <remarks>
    /// The audio level is calculated as the maximum absolute sample value normalized to 0-1 range.
    /// </remarks>
    public event EventHandler<float>? AudioLevelChanged;

    /// <summary>
    /// Gets whether audio is currently being recorded.
    /// </summary>
    public bool IsRecording => _isRecording;

    /// <summary>
    /// Gets the duration of the last recording in seconds.
    /// </summary>
    public double LastRecordingDuration { get; private set; }
    
    /// <summary>
    /// Gets whether the pre-buffer is currently active.
    /// </summary>
    public bool IsPreBuffering => _isPreBuffering;

    /// <summary>
    /// Constructor initializes the pre-buffer.
    /// </summary>
    public AudioService()
    {
        // Pre-buffer size: 300ms at 16kHz, 16-bit, mono = 9600 bytes
        // Add some extra margin
        _preBufferSize = (SampleRate * BytesPerSample * PreBufferMs) / 1000;
        _preBuffer = new byte[_preBufferSize];
    }

    /// <summary>
    /// Starts the pre-recording buffer to capture audio before trigger.
    /// </summary>
    public void StartPreBuffer()
    {
        if (_isPreBuffering || _disposed) return;
        
        try
        {
            _preBufferWritePos = 0;
            Array.Clear(_preBuffer, 0, _preBuffer.Length);
            
            _preBufferWaveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, 16, 1)
            };
            _preBufferWaveIn.DataAvailable += OnPreBufferDataAvailable;
            _preBufferWaveIn.StartRecording();
            _isPreBuffering = true;
            LoggingService.Trace("Pre-buffer started");
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to start pre-buffer");
            CleanupPreBuffer();
        }
    }
    
    /// <summary>
    /// Stops the pre-recording buffer.
    /// </summary>
    public void StopPreBuffer()
    {
        if (!_isPreBuffering) return;
        
        CleanupPreBuffer();
        _isPreBuffering = false;
        LoggingService.Trace("Pre-buffer stopped");
    }
    
    private void OnPreBufferDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isPreBuffering) return;
        
        // Write to circular buffer
        int bytesToWrite = Math.Min(e.BytesRecorded, _preBuffer.Length);
        int remaining = _preBuffer.Length - _preBufferWritePos;
        
        if (remaining >= bytesToWrite)
        {
            Array.Copy(e.Buffer, 0, _preBuffer, _preBufferWritePos, bytesToWrite);
            _preBufferWritePos = (_preBufferWritePos + bytesToWrite) % _preBuffer.Length;
        }
        else
        {
            // Wrap around
            Array.Copy(e.Buffer, 0, _preBuffer, _preBufferWritePos, remaining);
            Array.Copy(e.Buffer, remaining, _preBuffer, 0, bytesToWrite - remaining);
            _preBufferWritePos = bytesToWrite - remaining;
        }
    }
    
    private void CleanupPreBuffer()
    {
        try
        {
            _preBufferWaveIn?.StopRecording();
            _preBufferWaveIn?.Dispose();
            _preBufferWaveIn = null;
        }
        catch { }
    }

    /// <summary>
    /// Writes a proper WAV file header
    /// </summary>
    private void WriteWavHeader(Stream stream, long audioDataLength, int sampleRate, int bitsPerSample, int channels)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int chunkSize = 16; // For PCM
        
        // RIFF header
        writer.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
        writer.Write((int)(audioDataLength + 36)); // File size - 8
        writer.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));
        
        // fmt chunk
        writer.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
        writer.Write(chunkSize);
        writer.Write((short)1); // Audio format - PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);
        
        // data chunk
        writer.Write(System.Text.Encoding.UTF8.GetBytes("data"));
        writer.Write((int)audioDataLength);
    }

    /// <summary>
    /// Gets a list of available audio input devices.
    /// </summary>
    /// <returns>List of available audio devices with ID and name.</returns>
    public List<AudioDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                
                // Common sample rates for audio devices
                var sampleRates = "8kHz, 11kHz, 16kHz, 22kHz, 44kHz, 48kHz";
                
                devices.Add(new AudioDeviceInfo
                {
                    Id = i.ToString(),
                    Name = capabilities.ProductName,
                    Channels = capabilities.Channels,
                    SampleRates = sampleRates,
                    BitsPerSample = 16, // Standard for WaveIn
                    Latency = 0, // Not available in WaveIn
                    SupportsExclusiveMode = false // WaveIn doesn't support exclusive mode
                });
            }
        }
        catch
        {
            // No audio devices
        }
        return devices;
    }

    /// <summary>
    /// Starts audio recording from the default or selected microphone.
    /// </summary>
    /// <remarks>
    /// Recording uses 16kHz, mono, 16-bit PCM format.
    /// Events: <see cref="RecordingStarted"/> is raised when recording begins.
    /// </remarks>
    public void StartRecording()
    {
        if (_isRecording || _disposed) return;

        try
        {
            // Reset the completion source for the new recording
            _recordingStoppedSource?.TrySetCanceled();
            _recordingStoppedSource = null;
            
            _audioBuffer = new MemoryStream();

            // Write pre-buffer content to the beginning of the recording
            if (_isPreBuffering && _preBuffer.Length > 0)
            {
                // Write from the current position (most recent audio)
                int preBufferBytes = _preBuffer.Length;
                _audioBuffer.Write(_preBuffer, 0, preBufferBytes);
                LoggingService.Trace($"Added {preBufferBytes} bytes from pre-buffer to recording");
            }

            // Use 16kHz, mono, 16-bit PCM as per PRD
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1)
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStoppedInternal;

            // Don't use WaveFileWriter - we'll write WAV manually
            // _waveWriter = new WaveFileWriter(_audioBuffer, _waveIn.WaveFormat);

            _recordingStartTime = DateTime.Now;
            _isRecording = true;
            _waveIn.StartRecording();

            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Recording error");
            Cleanup();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        LoggingService.Trace($"OnDataAvailable called. BytesRecorded={e.BytesRecorded}, _isRecording={_isRecording}");
        if (_audioBuffer != null && _isRecording)
        {
            try
            {
                // Write raw PCM directly to our buffer (not through WaveFileWriter)
                _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);
                LoggingService.Trace($"Wrote {e.BytesRecorded} bytes to buffer");

                // Calculate RMS level
                float maxLevel = 0;
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(e.Buffer, i);
                    float level = Math.Abs(sample / 32768f);
                    if (level > maxLevel) maxLevel = level;
                }

                AudioLevelChanged?.Invoke(this, maxLevel);
            }
            catch
            {
#if DEBUG
                LoggingService.Debug($"[DEBUG] Error in OnDataAvailable");
#endif
                // Ignore errors during data capture
            }
        }
    }

    private void OnRecordingStoppedInternal(object? sender, StoppedEventArgs e)
    {
#if DEBUG
        LoggingService.Debug($"[DEBUG] OnRecordingStoppedInternal called. _isRecording={_isRecording}, _audioBuffer.Length={_audioBuffer?.Length}");
#endif
        LastRecordingDuration = (DateTime.Now - _recordingStartTime).TotalSeconds;
        
        // Only invoke if we were actually recording
        if (_isRecording)
        {
            _isRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);
            
            // Signal the waiting task that recording has stopped
            _recordingStoppedSource?.TrySetResult(true);
        }
        
        // DO NOT call Cleanup() here! StopRecordingAsync will handle cleanup after saving audio.
        // Calling Cleanup() here was setting _audioBuffer = null before we could save it.
    }

    /// <summary>
    /// Stops the current recording and saves the audio to a temporary WAV file.
    /// </summary>
    /// <returns>Path to the saved WAV file, or null if no recording was active.</returns>
    /// <remarks>
    /// The returned file is saved in the system temp directory with a timestamped name.
    /// Caller is responsible for cleaning up the temporary file after processing.
    /// </remarks>
    public async Task<string?> StopRecordingAsync()
    {
#if DEBUG
        LoggingService.Debug($"[DEBUG] StopRecordingAsync called. _isRecording={_isRecording}, _disposed={_disposed}");
#endif
        if (!_isRecording || _disposed) 
        {
#if DEBUG
            LoggingService.Debug("[DEBUG] StopRecordingAsync returning null - not recording or disposed");
#endif
            return null;
        }

        try
        {
            // Stop recording on the waveIn thread
            if (_waveIn != null)
            {
                try
                {
#if DEBUG
                    LoggingService.Debug("[DEBUG] Calling _waveIn.StopRecording()");
#endif
                    _waveIn.StopRecording();
                LoggingService.Debug("_waveIn.StopRecording() completed");
            }
            catch (Exception ex)
            {
                LoggingService.Debug($"Exception in StopRecording: {ex.Message}");
                // Ignore errors when stopping
            }
            }
            else
            {
                LoggingService.Warn("_waveIn is null - cannot stop");
            }

            // Wait for the recording to fully stop using proper synchronization
            // Create a completion source to wait for recording to stop
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _recordingStoppedSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            // Wait for the recording to fully stop using proper synchronization
            try
            {
                await _recordingStoppedSource.Task.WaitAsync(TimeSpan.FromSeconds(5), timeoutCts.Token);
            }
            catch (TimeoutException)
            {
                LoggingService.Warn("Recording stop timed out after 5 seconds");
            }
            catch (OperationCanceledException)
            {
                // Timeout was canceled, recording likely stopped
            }

            // Copy audio buffer - we now write raw PCM and will add WAV header manually
            MemoryStream? pcmData = null;
            if (_audioBuffer != null && _audioBuffer.Length > 0)
            {
                pcmData = new MemoryStream();
                _audioBuffer.Seek(0, SeekOrigin.Begin);
                _audioBuffer.CopyTo(pcmData);
#if DEBUG
                LoggingService.Debug($"[DEBUG] Copied PCM data: {pcmData.Length} bytes");
#endif
            }

            // Now save with proper WAV header
            if (pcmData != null && pcmData.Length > 0)
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"whisper_{DateTime.Now:yyyyMMddHHmmssfff}.wav");
                
                // Write WAV file with proper header
                using var fileStream = File.Create(tempPath);
                WriteWavHeader(fileStream, pcmData.Length, 16000, 16, 1);
                pcmData.Seek(0, SeekOrigin.Begin);
                pcmData.CopyTo(fileStream);

                LoggingService.Debug($"Saved audio to: {tempPath}");
                return tempPath;
            }
            else
            {
                LoggingService.Warn("No audio data to save");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Stop recording error");
        }
        finally
        {
            Cleanup();
        }

        return null;
    }

    private void Cleanup()
    {
        // Don't check _disposed here - cleanup can be called multiple times during a session
        
        // Note: We no longer use WaveFileWriter - we write WAV manually
        // Removed _waveWriter cleanup

        if (_waveIn != null)
        {
            try
            {
                // Remove event handlers to prevent callbacks during dispose
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStoppedInternal;
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"[Audio] Error removing event handlers: {ex.Message}");
            }

            try
            {
                _waveIn.Dispose();
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"[Audio] Error disposing WaveIn: {ex.Message}");
            }
            _waveIn = null;
        }

        try
        {
            _audioBuffer?.Dispose();
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"[Audio] Error disposing audio buffer: {ex.Message}");
        }
        _audioBuffer = null;

        // Note: Don't set _disposed = true here - it's only set in Dispose()
        // This allows multiple record/stop cycles within the same session
    }

    public void Dispose()
    {
        Cleanup();
        _disposed = true;
    }
}

    /// <summary>
    /// Represents information about an available audio input device.
    /// </summary>
    public class AudioDeviceInfo
    {
        /// <summary>
        /// The device identifier.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// The display name of the device.
        /// </summary>
        public string Name { get; set; } = "";
        
        /// <summary>
        /// Number of audio channels (e.g., 1 for mono, 2 for stereo).
        /// </summary>
        public int Channels { get; set; } = 1;
        
        /// <summary>
        /// Supported sample rates (comma-separated).
        /// </summary>
        public string SampleRates { get; set; } = "";
        
        /// <summary>
        /// Bits per sample (e.g., 16, 24, 32).
        /// </summary>
        public int BitsPerSample { get; set; } = 16;
        
        /// <summary>
        /// Device latency in milliseconds.
        /// </summary>
        public int Latency { get; set; } = 0;
        
        /// <summary>
        /// Whether the device supports exclusive mode.
        /// </summary>
        public bool SupportsExclusiveMode { get; set; } = false;
        
        /// <summary>
        /// Gets a formatted string showing channels and sample rate.
        /// </summary>
        public string FormatInfo => $"{SampleRates} · {(Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels} ch")}";
        
        /// <summary>
        /// Gets diagnostic info string.
        /// </summary>
        public string DiagnosticsInfo => $"Sample Rate: {SampleRates} | Bits: {BitsPerSample}-bit | Latency: {Latency}ms | Exclusive: {(SupportsExclusiveMode ? "Yes" : "No")}";
    }
