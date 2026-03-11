using System.IO;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using whisperMeOff.Services;
using whisperMeOff.Views;

namespace whisperMeOff;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private RecordingOverlayWindow? _recordingOverlay;
    private System.Windows.Controls.MenuItem? _recordMenuItem;
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
    
    // Track whether a transcription is in progress
    public static bool IsTranscribing { get; set; } = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        Settings.Load();
        
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
                        System.Diagnostics.Debug.WriteLine("[Startup] Registered for launch at login on app start");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Startup] Error: {ex.Message}");
            }
        }
        
        // Apply saved theme
        Theme.ApplyTheme(Settings.General.Theme);

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
        Database.Initialize();

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

        // Initialize async services
        Task.Run(async () =>
        {
            await Whisper.InitializeAsync();
            if (Settings.Llama.Enabled && !string.IsNullOrEmpty(Settings.Llama.ModelPath))
            {
                await Llama.InitializeAsync(Settings.Llama.ModelPath);
            }
        });
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
            var logPath = Path.Combine(AppDataPath, "error.log");
            var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n";
            File.AppendAllText(logPath, message);
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[ERROR] {source}: {ex.Message}");
#endif
        }
        catch
        {
            // Ignore logging failures
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
                System.Diagnostics.Debug.WriteLine("[DEBUG] Tray menu clicked but transcription in progress, ignoring");
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
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] Error loading icon: {ex.Message}");
        }
        
        _notifyIcon.Text = isRecording ? "whisperMeOff - Recording..." : "whisperMeOff - Ready";
    }

    private void OnRecordingStarted()
    {
        Dispatcher.Invoke(() =>
        {
            _recordingOverlay?.Show();
            UpdateTrayIcon(true);
            if (_recordMenuItem != null)
                _recordMenuItem.Header = "Stop Recording";
        });
    }

    private void OnRecordingStopped()
    {
        Dispatcher.Invoke(() =>
        {
            _recordingOverlay?.Hide();
            UpdateTrayIcon(false);
            if (_recordMenuItem != null)
                _recordMenuItem.Header = "Start Recording";
        });
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Don't allow starting new recording while processing
            if (IsTranscribing)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Hotkey pressed but transcription in progress, ignoring");
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
        Dispatcher.Invoke(() =>
        {
            if (Settings.General.PushToTalkMode && Audio.IsRecording)
            {
                _ = StopRecordingAndTranscribeAsync();
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

            // Copy to clipboard
            var previousClipboard = Clipboard.GetText();
            Clipboard.SetText(text);

            // Auto-paste (now uses retry mechanism internally)
            await Clipboard.PasteToWindow(targetWindow);

            // Restore previous clipboard after shorter delay
            await Task.Delay(50);
            if (!string.IsNullOrEmpty(previousClipboard))
            {
                Clipboard.SetText(previousClipboard);
            }

            // Save to database
            await Database.AddTranscriptionAsync(text, Audio.LastRecordingDuration, Settings.Whisper.ModelPath, Settings.Whisper.Language);

            // Update main window
            Dispatcher.Invoke(() =>
            {
                _mainWindow?.UpdateLastTranscription(text);
            });

            // Cleanup temp file
            if (File.Exists(audioFile))
            {
                File.Delete(audioFile);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Transcription error: {ex.Message}");
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
        ExitApplication();
        base.OnExit(e);
    }
}
