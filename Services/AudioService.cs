using NAudio.Wave;
using System.IO;

namespace whisperMeOff.Services;

public class AudioService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private DateTime _recordingStartTime;
    private bool _isRecording;
    private bool _disposed;

    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingStopped;
    public event EventHandler<float>? AudioLevelChanged;

    public bool IsRecording => _isRecording;
    public double LastRecordingDuration { get; private set; }

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

    public List<AudioDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                devices.Add(new AudioDeviceInfo
                {
                    Id = i.ToString(),
                    Name = capabilities.ProductName
                });
            }
        }
        catch
        {
            // No audio devices
        }
        return devices;
    }

    public void StartRecording()
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] StartRecording called. _isRecording={_isRecording}, _disposed={_disposed}, _waveIn={_waveIn}");
        if (_isRecording || _disposed) return;

        try
        {
            _audioBuffer = new MemoryStream();

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
            System.Diagnostics.Debug.WriteLine($"Recording error: {ex.Message}");
            Cleanup();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] OnDataAvailable called. BytesRecorded={e.BytesRecorded}, _isRecording={_isRecording}");
        if (_audioBuffer != null && _isRecording)
        {
            try
            {
                // Write raw PCM directly to our buffer (not through WaveFileWriter)
                _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Wrote {e.BytesRecorded} bytes to buffer");

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Error in OnDataAvailable: {ex.Message}");
                // Ignore errors during data capture
            }
        }
    }

    private void OnRecordingStoppedInternal(object? sender, StoppedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] OnRecordingStoppedInternal called. _isRecording={_isRecording}, _audioBuffer.Length={_audioBuffer?.Length}");
        LastRecordingDuration = (DateTime.Now - _recordingStartTime).TotalSeconds;
        
        // Only invoke if we were actually recording
        if (_isRecording)
        {
            _isRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }
        
        // DO NOT call Cleanup() here! StopRecordingAsync will handle cleanup after saving audio.
        // Calling Cleanup() here was setting _audioBuffer = null before we could save it.
    }

    public async Task<string?> StopRecordingAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] StopRecordingAsync called. _isRecording={_isRecording}, _disposed={_disposed}");
        if (!_isRecording || _disposed) 
        {
            System.Diagnostics.Debug.WriteLine("[DEBUG] StopRecordingAsync returning null - not recording or disposed");
            return null;
        }

        try
        {
            // Stop recording on the waveIn thread
            if (_waveIn != null)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] Calling _waveIn.StopRecording()");
                    _waveIn.StopRecording();
                    System.Diagnostics.Debug.WriteLine("[DEBUG] _waveIn.StopRecording() completed");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Exception in StopRecording: {ex.Message}");
                    // Ignore errors when stopping
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] _waveIn is null - cannot stop");
            }

            // Wait for the recording to fully stop
            await Task.Delay(200);

            // Copy audio buffer - we now write raw PCM and will add WAV header manually
            MemoryStream? pcmData = null;
            if (_audioBuffer != null && _audioBuffer.Length > 0)
            {
                pcmData = new MemoryStream();
                _audioBuffer.Seek(0, SeekOrigin.Begin);
                _audioBuffer.CopyTo(pcmData);
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Copied PCM data: {pcmData.Length} bytes");
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

                System.Diagnostics.Debug.WriteLine($"[DEBUG] Saved audio to: {tempPath}");
                return tempPath;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] No audio data to save");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Stop recording error: {ex.Message}");
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
            catch { }

            try
            {
                _waveIn.Dispose();
            }
            catch { }
            _waveIn = null;
        }

        try
        {
            _audioBuffer?.Dispose();
        }
        catch { }
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

public class AudioDeviceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
