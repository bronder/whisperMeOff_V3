using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using whisperMeOff.Services;
using whisperMeOff.Views;

namespace whisperMeOff;

public partial class App : System.Windows.Application
{
    private TaskbarIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private RecordingOverlayWindow? _recordingOverlay;
    private System.Windows.Controls.MenuItem? _recordMenuItem;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

    private void SetupSystemTray()
    {
        _notifyIcon = new TaskbarIcon
        {
            ToolTipText = "whisperMeOff - Ready",
            Visibility = Visibility.Visible
        };

        // Create context menu
        var contextMenu = new System.Windows.Controls.ContextMenu();

        // Recording controls
        _recordMenuItem = new System.Windows.Controls.MenuItem { Header = "Start Recording" };
        _recordMenuItem.Click += async (s, e) =>
        {
            if (App.Audio.IsRecording)
            {
                // Stop recording and transcribe
                var audioFile = await App.Audio.StopRecordingAsync();
                if (!string.IsNullOrEmpty(audioFile))
                {
                    _ = App.Whisper.TranscribeAsync(audioFile);
                }
                _recordMenuItem.Header = "Start Recording";
            }
            else
            {
                // Start recording
                App.Audio.StartRecording();
                _recordMenuItem.Header = "Stop Recording";
            }
        };
        contextMenu.Items.Add(_recordMenuItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show whisperMeOff" };
        showItem.Click += (s, e) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
        };
        contextMenu.Items.Add(showItem);

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (s, e) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
            _mainWindow?.NavigateToSettings();
        };
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (s, e) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenu = contextMenu;
        _notifyIcon.TrayMouseDoubleClick += (s, e) =>
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

        // Create a simple icon based on state
        var icon = CreateTrayIcon(isRecording);
        _notifyIcon.Icon = icon;
        _notifyIcon.ToolTipText = isRecording ? "whisperMeOff - Recording..." : "whisperMeOff - Ready";
    }

    private System.Drawing.Icon CreateTrayIcon(bool isRecording)
    {
        // Create a simple 16x16 icon
        using var bitmap = new System.Drawing.Bitmap(16, 16);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);

        graphics.Clear(isRecording ? System.Drawing.Color.Red : System.Drawing.Color.FromArgb(0, 212, 255));

        // Draw a simple microphone shape
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        graphics.FillEllipse(brush, 5, 2, 6, 8);
        graphics.FillRectangle(brush, 6, 10, 4, 3);

        var handle = bitmap.GetHicon();
        return System.Drawing.Icon.FromHandle(handle);
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

            // Auto-paste
            await Task.Delay(50);
            Clipboard.PasteToWindow(targetWindow);

            // Restore previous clipboard after delay
            await Task.Delay(100);
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
