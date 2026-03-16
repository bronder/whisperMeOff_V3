using System.IO;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using whisperMeOff.Interfaces;
using whisperMeOff.Services;
using whisperMeOff.Views;

namespace whisperMeOff;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private RecordingOverlayWindow? _recordingOverlay;
    private ToolStripMenuItem? _recordToolStripItem;

    public static string AppDataPath { get; private set; } = null!;
    public static string ModelsPath { get; private set; } = null!;
    public static string WhisperModelsPath { get; private set; } = null!;
    public static string LlamaModelsPath { get; private set; } = null!;
    public static string SettingsPath { get; private set; } = null!;
    public static string DatabasePath { get; private set; } = null!;

    public static SettingsService Settings { get; private set; } = null!;
    public static ThemeService Theme { get; private set; } = null!;
    public static AudioService Audio { get; private set; } = null!;
    public static WhisperService Whisper { get; private set; } = null!;
    public static LlamaService Llama { get; private set; } = null!;
    public static DatabaseService Database { get; private set; } = null!;
    public static HotkeyService Hotkey { get; private set; } = null!;
    public static ClipboardService Clipboard { get; private set; } = null!;
    public static ModelDownloadService ModelDownload { get; private set; } = null!;
    
    private static TextTransformationService? _transform;
    public static TextTransformationService Transform => _transform ??= new TextTransformationService((ILlamaService)Llama);
    
    // Track whether a transcription is in progress
    public static bool IsTranscribing { get; set; } = false;

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            // Initialize logging FIRST before anything else
            LoggingService.Initialize();

            // Setup global exception handlers
            SetupExceptionHandling();

        // Setup application data paths
        AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "whisperMeOff");
        Directory.CreateDirectory(AppDataPath);

        ModelsPath = Path.Combine(AppDataPath, "models");
        WhisperModelsPath = Path.Combine(ModelsPath, "whisper");
        LlamaModelsPath = Path.Combine(ModelsPath, "llama");
        Directory.CreateDirectory(WhisperModelsPath);
        Directory.CreateDirectory(LlamaModelsPath);

        SettingsPath = Path.Combine(AppDataPath, "settings.json");
        DatabasePath = Path.Combine(AppDataPath, "transcriptions.db");

        // Initialize services
        Settings = new SettingsService();
        Theme = new ThemeService();
        Database = new DatabaseService();
        Audio = new AudioService();
        Whisper = new WhisperService();
        Llama = new LlamaService();
        Clipboard = new ClipboardService();
        Hotkey = new HotkeyService();
        ModelDownload = new ModelDownloadService();

        // Load settings
        try
        {
            Settings.Load();
            LoggingService.Info("[Startup] Settings loaded successfully");
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[Startup] Error loading settings - using defaults");
        }
        
        // Check if first run - show onboarding window
        if (!Settings.General.HasCompletedOnboarding)
        {
            LoggingService.Info("[Startup] First run detected - showing onboarding");
            try
            {
                var firstRunWindow = new FirstRunWindow();
                firstRunWindow.ShowDialog();
                
                // If user cancelled, exit the app
                if (firstRunWindow.DialogResult != true)
                {
                    LoggingService.Info("[Startup] User cancelled onboarding - exiting");
                    Shutdown();
                    return;
                }
                
                // Reload settings after FirstRunWindow saves changes
                Settings.Load();
                LoggingService.Info("[Startup] Settings reloaded after onboarding");
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "[Startup] Error showing FirstRunWindow");
                // Continue anyway - show the error but try to proceed
                System.Windows.MessageBox.Show($"Error during first-run setup: {ex.Message}", "Warning", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
        
        // Apply startup registration if enabled
        if (Settings.General.LaunchAtLogin)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("whisperMeOff", $"\"{exePath}\"");
                        LoggingService.Info("[Startup] Registered for launch at login on app start");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "[Startup] Error");
            }
        }
        
        // Apply saved theme
        try
        {
            Theme.ApplyTheme(Settings.General.Theme);
            LoggingService.Info($"[Startup] Applied theme: {Settings.General.Theme}");
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, $"[Startup] Failed to apply theme: {Settings.General.Theme}");
        }

        // Update model paths based on settings - use custom paths if set
        var customWhisperPath = Settings.General.ModelDownloadPath;
        var customLlamaPath = Settings.General.LlamaDownloadPath;
        
        // Handle Whisper path
        if (!string.IsNullOrEmpty(customWhisperPath) && Directory.Exists(customWhisperPath))
        {
            WhisperModelsPath = customWhisperPath;
            Directory.CreateDirectory(WhisperModelsPath);
        }
        
        // Handle Llama path (separate from Whisper path)
        if (!string.IsNullOrEmpty(customLlamaPath) && Directory.Exists(customLlamaPath))
        {
            LlamaModelsPath = customLlamaPath;
            Directory.CreateDirectory(LlamaModelsPath);
        }
        else if (!string.IsNullOrEmpty(customWhisperPath) && Directory.Exists(customWhisperPath))
        {
            // Fall back to Whisper path's llama subfolder if no custom Llama path set
            LlamaModelsPath = Path.Combine(ModelsPath, "llama");
            Directory.CreateDirectory(LlamaModelsPath);
        }

        // Initialize database
        await Database.InitializeAsync();

        // Setup system tray
        SetupSystemTray();

        // Setup main window
        _mainWindow = new MainWindow();
        _mainWindow.Closing += (s, args) =>
        {
            args.Cancel = true;
            _mainWindow.Hide();
        };

        // Setup recording overlay
        _recordingOverlay = new RecordingOverlayWindow();

        // Subscribe to recording events
        Audio.RecordingStarted += (s, e) => OnRecordingStarted();
        Audio.RecordingStopped += (s, e) => OnRecordingStopped();

        // Subscribe to hotkey events
        Hotkey.HotkeyPressed += (s, e) => OnHotkeyPressed(s, EventArgs.Empty);
        Hotkey.HotkeyReleased += (s, e) => OnHotkeyReleased(s, EventArgs.Empty);

        // Start the app
        _mainWindow.Show();

        // Initialize async services (fire and forget - these load ML models)
        _ = Task.Run(async () =>
        {
            await Whisper.InitializeAsync();
            if (Settings.Llama.Enabled && !string.IsNullOrEmpty(Settings.Llama.ModelPath))
            {
                await Llama.InitializeAsync(Settings.Llama.ModelPath);
            }
        });
        }
        catch (Exception ex)
        {
            LoggingService.Fatal(ex, "[Startup] Unexpected error during application startup");
            System.Windows.MessageBox.Show($"An unexpected error occurred during startup: {ex.Message}\n\nThe application will now exit.", "Startup Error", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private void SetupExceptionHandling()
    {
        // Handle unhandled exceptions in AppDomain
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogException((Exception)e.ExceptionObject, "AppDomain.UnhandledException");
            if (e.IsTerminating)
            {
                Environment.Exit(1);
            }
        };

        // Handle unhandled exceptions in WPF dispatcher
        DispatcherUnhandledException += (s, e) =>
        {
            LogException(e.Exception, "Dispatcher.UnhandledException");
            e.Handled = true;
            ShowErrorMessage("An unexpected error occurred. The application will continue running.");
        };

        // Handle unobserved task exceptions
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
    }

    private void LogException(Exception ex, string source)
    {
        try
        {
            // Use NLog for structured logging
            LoggingService.Fatal(ex, $"Unhandled exception from {source}");
        }
        catch
        {
            // Fallback to basic file logging if NLog fails
            var logPath = Path.Combine(AppDataPath, "error.log");
            var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n";
            File.AppendAllText(logPath, message);
        }
    }

    private void ShowErrorMessage(string message)
    {
        try
        {
            Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    message,
                    "whisperMeOff Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            });
        }
        catch
        {
            // Ignore UI failures
        }
    }

    private void SetupSystemTray()
    {
        // Use Windows Forms NotifyIcon for native context menu theming
        _notifyIcon = new NotifyIcon
        {
            Text = "whisperMeOff - Ready",
            Visible = true
        };

        // Create context menu using Windows Forms for native theming
        var contextMenu = new ContextMenuStrip();

        // Recording controls
        _recordToolStripItem = new ToolStripMenuItem("Start Recording");
        _recordToolStripItem.Click += async (s, e) =>
        {
            // Don't allow starting new recording while processing
            if (IsTranscribing)
            {
                LoggingService.Debug("[DEBUG] Tray menu clicked but transcription in progress, ignoring");
                return;
            }
            
            if (App.Audio.IsRecording)
            {
                // Stop recording and transcribe
                var audioFile = await App.Audio.StopRecordingAsync();
                if (!string.IsNullOrEmpty(audioFile))
                {
                    _ = App.Whisper.TranscribeAsync(audioFile);
                }
                _recordToolStripItem.Text = "Start Recording";
            }
            else
            {
                // Start recording
                App.Audio.StartRecording();
                _recordToolStripItem.Text = "Stop Recording";
            }
        };
        contextMenu.Items.Add(_recordToolStripItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var showItem = new ToolStripMenuItem("Show whisperMeOff");
        showItem.Click += (s, e) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
        };
        contextMenu.Items.Add(showItem);

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (s, e) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
            _mainWindow?.NavigateToSettings();
        };
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) =>
        {
            if (_mainWindow?.IsVisible == true)
                _mainWindow.Hide();
            else
            {
                _mainWindow?.Show();
                _mainWindow?.Activate();
            }
        };

        // Use a default icon (will be replaced with custom icon)
        // Create a simple microphone icon programmatically
        UpdateTrayIcon(false);
    }

    public void UpdateTrayIcon(bool isRecording)
    {
        if (_notifyIcon == null) return;

        // Use pre-built icons - no GDI handle leak
        try
        {
            var iconUri = isRecording 
                ? new Uri("pack://application:,,,/flatrobot_red.ico")
                : new Uri("pack://application:,,,/flatrobot_blue.ico");
            
            var iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[TrayIcon] Error loading icon");
        }
        
        _notifyIcon.Text = isRecording ? "whisperMeOff - Recording..." : "whisperMeOff - Ready";
    }

    private void OnRecordingStarted()
    {
        Dispatcher.Invoke(() =>
        {
            _recordingOverlay?.Show();
            _recordingOverlay?.StartRecordingTimer();
            UpdateTrayIcon(true);
            if (_recordToolStripItem != null)
                _recordToolStripItem.Text = "Stop Recording";
        });
    }

    private void OnRecordingStopped()
    {
        Dispatcher.Invoke(() =>
        {
            _recordingOverlay?.StopRecordingTimer();
            _recordingOverlay?.Hide();
            UpdateTrayIcon(false);
            if (_recordToolStripItem != null)
                _recordToolStripItem.Text = "Start Recording";
        });
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Don't allow starting new recording while processing
            if (IsTranscribing)
            {
                LoggingService.Debug("[DEBUG] Hotkey pressed but transcription in progress, ignoring");
                return;
            }
            
            if (Settings.General.PushToTalkMode)
            {
                // Push-to-talk mode: Start recording when hotkey pressed
                if (!Audio.IsRecording)
                {
                    Audio.StartRecording();
                }
            }
            else
            {
                // Toggle mode: Press to start/stop
                if (Audio.IsRecording)
                {
                    _ = StopRecordingAndTranscribeAsync();
                }
                else
                {
                    Audio.StartRecording();
                }
            }
        });
    }

    private async Task StopRecordingAndTranscribeAsync()
    {
        var audioFile = await Audio.StopRecordingAsync();
        if (!string.IsNullOrEmpty(audioFile))
        {
            await ProcessTranscriptionAsync(audioFile);
        }
    }

    private void OnHotkeyReleased(object? sender, EventArgs e)
    {
        // Push-to-talk: stop recording and transcribe when key is released
        LoggingService.Debug("[DEBUG] OnHotkeyReleased called");
        Dispatcher.Invoke(() =>
        {
            if (Settings.General.PushToTalkMode && Audio.IsRecording)
            {
                LoggingService.Debug("[DEBUG] PushToTalk mode - calling StopRecordingAndTranscribeAsync");
                _ = StopRecordingAndTranscribeAsync();
            }
            else
            {
                LoggingService.Debug($"[DEBUG] Not in PushToTalk mode or not recording. PushToTalkMode={Settings.General.PushToTalkMode}, IsRecording={Audio.IsRecording}");
            }
        });
    }

    private async Task ProcessTranscriptionAsync(string audioFile)
    {
        try
        {
            IsTranscribing = true;
            
            // Get previous window for auto-paste
            var targetWindow = Hotkey.GetPreviousWindow();

            // Transcribe
            var text = await Whisper.TranscribeAsync(audioFile);

            // Apply Llama formatting if enabled
            if (Settings.Llama.Enabled && Llama.IsLoaded)
            {
                text = await Llama.FormatTextAsync(text);
            }

            // Run clipboard operations on UI thread via Dispatcher (same as MainWindow.xaml.cs)
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Use ClipboardService for proper STA thread handling
                    var previousClipboard = App.Clipboard.GetText();
                    
                    if (string.IsNullOrEmpty(text))
                    {
                        return;
                    }
                    
                    // Set the transcribed text to clipboard
                    App.Clipboard.SetText(text);

                    // Paste to previous window (now uses retry mechanism internally)
                    await App.Clipboard.PasteToWindow(targetWindow);

                    // Restore previous clipboard after delay (if enabled)
                    if (Settings.General.RestoreClipboard && !string.IsNullOrEmpty(previousClipboard))
                    {
                        var delay = Settings.General.ClipboardRestoreDelayMs;
                        if (delay > 0)
                        {
                            await Task.Delay(delay);
                            App.Clipboard.SetText(previousClipboard);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Error(ex, "[CLIPBOARD] Error in hotkey transcription");
                }
            });

            // Save to database (pass 0 for processing time - can be enhanced later)
            await Database.AddTranscriptionAsync(text, Audio.LastRecordingDuration, 0, Settings.Whisper.ModelPath, Settings.Whisper.Language);

            // Update main window
            LoggingService.Debug($"[DEBUG] ProcessTranscriptionAsync: Updating UI with text: '{text}'");
            Dispatcher.Invoke(() =>
            {
                if (_mainWindow == null)
                {
                    LoggingService.Debug("[DEBUG] _mainWindow is NULL!");
                }
                else
                {
                    _mainWindow.UpdateLastTranscription(text ?? string.Empty);
                }
            });

            // Cleanup temp file
            if (File.Exists(audioFile))
            {
                File.Delete(audioFile);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Transcription error");
            LoggingService.Debug($"[DEBUG] ProcessTranscriptionAsync EXCEPTION: {ex.Message}");
            LoggingService.Debug($"[DEBUG] Stack trace: {ex.StackTrace}");
        }
        finally
        {
            IsTranscribing = false;
        }
    }

    public void ExitApplication()
    {
        // Cleanup
        Audio.Dispose();
        Whisper.Dispose();
        Llama.Dispose();
        Hotkey.Dispose();
        Database.Dispose();
        _notifyIcon?.Dispose();
        _recordingOverlay?.Close();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Shutdown logging
        LoggingService.Shutdown();
        
        ExitApplication();
        base.OnExit(e);
    }
}

/// <summary>
/// Converts boolean to Visibility
/// </summary>
public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}

/// <summary>
/// Converts boolean to inverse Visibility
/// </summary>
public class InverseBoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return true;
    }
}

/// <summary>
/// Converts int to bool (for checking if length > 0)
/// </summary>
public class IntToBoolConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
