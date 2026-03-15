using System.Windows;
using System.Windows.Controls;
using System.Linq;
using Microsoft.Win32;
using whisperMeOff.Services;
using whisperMeOff.Models.Transformation;

namespace whisperMeOff.Views;

public partial class MainWindow : Window
{
    private bool _isProcessing = false;
    private bool _isMultiSelectMode = false;
    
    public bool IsMultiSelectMode
    {
        get => _isMultiSelectMode;
        set
        {
            _isMultiSelectMode = value;
            // Clear selections when exiting multi-select mode
            if (!value)
            {
                foreach (var item in HistoryListBox.Items.OfType<TranscriptionListItem>())
                {
                    item.IsSelected = false;
                }
            }
        }
    }
    
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
    }
    
    private void UpdateLlamaModelBadges(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            LlamaModelBadgesPanel.Visibility = Visibility.Collapsed;
            return;
        }
        
        var filename = System.IO.Path.GetFileName(modelPath);
        
        // Parse quantization from filename (e.g., Q4_K_M, Q5_K_S, Q8_0, fp16, f16, f32)
        var quantization = "Unknown";
        
        // Try standard quantization patterns
        var quantMatch = System.Text.RegularExpressions.Regex.Match(filename, @"\.([QKq][0-9]+_[A-Z]+)\.");
        if (!quantMatch.Success)
            quantMatch = System.Text.RegularExpressions.Regex.Match(filename, @"(Q[0-9]+_[A-Z]+)");
        
        // Try FP16/F32 patterns
        if (!quantMatch.Success)
            quantMatch = System.Text.RegularExpressions.Regex.Match(filename, @"(fp16|f16|f32)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (quantMatch.Success)
        {
            quantization = quantMatch.Value.ToUpper();
        }
        
        // Parse model size from common patterns like 7B, 3B, 1.7B, 2B, 8B, 70B, 1.5b, etc.
        var size = "Unknown";
        double sizeValue = 0;
        
        // Try patterns like 7B, 3B, 1.7B (case insensitive, word boundary)
        var sizeMatch = System.Text.RegularExpressions.Regex.Match(filename, @"(\d+\.?\d*)\s*[bB]\b");
        if (!sizeMatch.Success)
            sizeMatch = System.Text.RegularExpressions.Regex.Match(filename, @"-(\d+\.?\d*)[bB]\b");
        
        if (sizeMatch.Success)
        {
            var sizeStr = sizeMatch.Groups[1].Value;
            if (double.TryParse(sizeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out sizeValue))
            {
                size = $"{sizeValue}B";
            }
        }
        
        // Estimate RAM usage based on quantization type
        double estimatedRAM = 0;
        if (sizeValue > 0)
        {
            if (quantization.Contains("FP16") || quantization == "F16")
                estimatedRAM = sizeValue * 2; // FP16 = 2 bytes per parameter
            else if (quantization == "F32")
                estimatedRAM = sizeValue * 4; // FP32 = 4 bytes per parameter
            else if (quantization.StartsWith("Q"))
                estimatedRAM = sizeValue * 2; // Q4 = ~2 bytes per parameter (average)
            else
                estimatedRAM = sizeValue * 2; // Default assumption
        }
        
        // Update badges
        QuantizationBadgeText.Text = quantization;
        SizeBadgeText.Text = size;
        RAMBadgeText.Text = $"~{estimatedRAM:F1} GB";
        
        LlamaModelBadgesPanel.Visibility = Visibility.Visible;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Escape to close popups or minimize window
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Close any open context menus or popups
            if (ContextMenuService.GetContextMenu(this)?.IsOpen == true)
            {
                ContextMenuService.GetContextMenu(this).IsOpen = false;
                e.Handled = true;
            }
            else if (WindowState == WindowState.Maximized)
            {
                // Optional: Escape minimizes when maximized
                // WindowState = WindowState.Normal;
                // e.Handled = true;
            }
        }
        
        // F1 for keyboard shortcuts help
        if (e.Key == System.Windows.Input.Key.F1)
        {
            ShowKeyboardShortcuts();
            e.Handled = true;
        }
    }

    private void ShowKeyboardShortcuts()
    {
        System.Windows.MessageBox.Show(
            "Keyboard Shortcuts:\n\n" +
            "Ctrl+Shift+R - Start/Stop Recording\n" +
            "Tab - Navigate between controls\n" +
            "Arrow Keys - Navigate lists\n" +
            "Space/Enter - Activate buttons\n" +
            "Escape - Close popups\n" +
            "F1 - Show this help",
            "Keyboard Shortcuts",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Prevent saves during initialization
        _isLoading = true;
        
        // Initialize hotkey
        App.Hotkey.Initialize(this);

        // Load audio devices
        LoadAudioDevices();
      
        // Load settings to UI
        LoadSettingsToUI();
        
        // Update preset button visual state based on current settings
        if (App.Settings?.Whisper?.ModelSize != null)
        {
            UpdatePresetButtons(App.Settings.Whisper.ModelSize);
        }
        
        // Done loading - now allow saves
        _isLoading = false;

        // Subscribe to Llama model load/unload events
        App.Llama.ModelLoaded += (s, isLoaded) => Dispatcher.Invoke(() =>
        {
            if (App.Settings?.Llama?.Enabled == true && LlamaStatusText != null)
            {
                LlamaStatusText.Text = isLoaded ? "Enabled (Loaded)" : "Enabled (Not Loaded)";
            }
        });

        // Load history
        await LoadHistoryAsync();

        // Subscribe to recording events to update button state
        App.Audio.RecordingStarted += (s, ev) => Dispatcher.Invoke(() =>
        {
            RecordButton.Content = "Stop Recording";
            StatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            AudioLevelMeter.Visibility = Visibility.Visible;
            AudioLevelMeter.Value = 0;
        });
        App.Audio.RecordingStopped += (s, ev) => Dispatcher.Invoke(() =>
        {
            RecordButton.Content = "Start Recording";
            StatusIndicator.Fill = (System.Windows.Media.Brush)FindResource("AccentBrush");
            AudioLevelMeter.Visibility = Visibility.Collapsed;
            AudioLevelMeter.Value = 0;
        });
        
        // Subscribe to audio level changes
        App.Audio.AudioLevelChanged += (s, level) => Dispatcher.Invoke(() =>
        {
            AudioLevelMeter.Value = level;
        });
    }

    private void LoadAudioDevices()
    {
        var devices = App.Audio.GetAvailableDevices();
        MicrophoneComboBox.Items.Clear();
        MicrophoneComboBox.Items.Add(new ComboBoxItem { Content = "Default Microphone", Tag = "" });

        foreach (var device in devices)
        {
            MicrophoneComboBox.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
        }

        // Select current device
        var currentDeviceId = App.Settings.Audio.DeviceId;
        for (int i = 0; i < MicrophoneComboBox.Items.Count; i++)
        {
            var item = (ComboBoxItem)MicrophoneComboBox.Items[i];
            if (item.Tag?.ToString() == currentDeviceId)
            {
                MicrophoneComboBox.SelectedIndex = i;
                break;
            }
        }
    }

    private void LoadSettingsToUI()
    {
        // Language
        var language = App.Settings.Whisper.Language;
        foreach (ComboBoxItem item in LanguageComboBox.Items)
        {
            if (item.Tag?.ToString() == language)
            {
                LanguageComboBox.SelectedItem = item;
                break;
            }
        }

        // Translate
        TranslateCheckbox.IsChecked = App.Settings.Whisper.Translate;

        // Custom Vocabulary
        CustomVocabularyTextBox.Text = App.Settings.Whisper.CustomVocabulary;

        // Word Replacements
        ReplacementsListBox.ItemsSource = App.Settings.Whisper.WordReplacements;

        // Model Download Path
        var downloadPath = App.Settings.General.ModelDownloadPath;
        if (!string.IsNullOrEmpty(downloadPath))
        {
            ModelDownloadPathTextBox.Text = downloadPath;
            DownloadPathSummaryText.Text = $"Using: {downloadPath}";
        }
        else
        {
            DownloadPathSummaryText.Text = $"Default: {App.WhisperModelsPath}";
        }

        // Llama Model Download Path
        var llamaDownloadPath = App.Settings.General.LlamaDownloadPath;
        if (!string.IsNullOrEmpty(llamaDownloadPath))
        {
            LlamaDownloadPathTextBox.Text = llamaDownloadPath;
            LlamaDownloadPathSummaryText.Text = $"Using: {llamaDownloadPath}";
        }
        else
        {
            LlamaDownloadPathSummaryText.Text = $"Default: {App.LlamaModelsPath}";
        }

        // Model path
        var modelPath = App.Whisper.GetModelPath();
        if (!string.IsNullOrEmpty(modelPath))
        {
            ModelPathText.Text = System.IO.Path.GetFileName(modelPath);
            WhisperStatusText.Text = "Ready";
            UpdateModelInfo(modelPath);
        }

        // Llama status - show whether enabled and loaded
        if (App.Settings.Llama.Enabled)
        {
            if (App.Llama.IsLoaded)
            {
                LlamaStatusText.Text = "Enabled (Loaded)";
            }
            else
            {
                LlamaStatusText.Text = "Enabled (Not Loaded)";
            }
        }
        else
        {
            LlamaStatusText.Text = "Disabled";
        }

        // Check which Whisper models are already downloaded
        var whisperPath = App.WhisperModelsPath;
        if (System.IO.Directory.Exists(whisperPath))
        {
            var tinyPath = System.IO.Path.Combine(whisperPath, "ggml-tiny.bin");
            var basePath = System.IO.Path.Combine(whisperPath, "ggml-base.bin");
            var smallPath = System.IO.Path.Combine(whisperPath, "ggml-small.bin");
            var mediumPath = System.IO.Path.Combine(whisperPath, "ggml-medium.bin");
            var largePath = System.IO.Path.Combine(whisperPath, "ggml-large.bin");
            var largeV3Path = System.IO.Path.Combine(whisperPath, "ggml-large-v3.bin");

            if (System.IO.File.Exists(tinyPath))
            {
                DownloadTinyBtn.Content = "Downloaded";
                DownloadTinyBtn.IsEnabled = false;
            }
            if (System.IO.File.Exists(basePath))
            {
                DownloadBaseBtn.Content = "Downloaded";
                DownloadBaseBtn.IsEnabled = false;
            }
            if (System.IO.File.Exists(smallPath))
            {
                DownloadSmallBtn.Content = "Downloaded";
                DownloadSmallBtn.IsEnabled = false;
            }
            if (System.IO.File.Exists(mediumPath))
            {
                DownloadMediumBtn.Content = "Downloaded";
                DownloadMediumBtn.IsEnabled = false;
            }
            if (System.IO.File.Exists(largePath) || System.IO.File.Exists(largeV3Path))
            {
                DownloadLargeBtn.Content = "Downloaded";
                DownloadLargeBtn.IsEnabled = false;
            }
        }

        // Llama settings
        LlamaEnableCheckbox.IsChecked = App.Settings.Llama.Enabled;
        var llamaPath = App.Settings.Llama.ModelPath;
        if (!string.IsNullOrEmpty(llamaPath))
        {
            LlamaModelPathText.Text = System.IO.Path.GetFileName(llamaPath);
            UpdateLlamaModelBadges(llamaPath);
        }

        // Llama translation settings
        LlamaTranslateCheckbox.IsChecked = App.Settings.Llama.Translate;
        var targetLang = App.Settings.Llama.TranslateTo ?? "en";
        // Set the combo box selection based on saved setting
        for (int i = 0; i < LlamaTargetLanguageComboBox.Items.Count; i++)
        {
            if (LlamaTargetLanguageComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == targetLang)
            {
                LlamaTargetLanguageComboBox.SelectedIndex = i;
                break;
            }
        }

        // HuggingFace Model ID
        var modelId = App.Settings.Llama.ModelId;
        if (!string.IsNullOrEmpty(modelId))
        {
            HuggingFaceModelIdTextBox.Text = modelId;
        }

        // HuggingFace Token (load into PasswordBox)
        var hfToken = App.Settings.Llama.HuggingFaceToken;
        
        // If token looks encrypted (very long base64 string), try to decrypt it first
        if (!string.IsNullOrEmpty(hfToken) && hfToken.Length > 128)
        {
            // Try to decrypt - the token might still be encrypted in settings
            try
            {
                var decrypted = Services.SettingsService.Decrypt(hfToken);
                if (decrypted.Length <= 128)
                {
                    hfToken = decrypted;
                    App.Settings.Llama.HuggingFaceToken = decrypted; // Update settings with decrypted value
                }
                else
                {
                    // Still too long after decrypt attempt - clear it
                    LoggingService.Warn("[Settings] HuggingFaceToken still too long after decrypt, clearing");
                    hfToken = "";
                    App.Settings.Llama.HuggingFaceToken = "";
                }
            }
            catch
            {
                // Decrypt failed - clear corrupted token
                LoggingService.Warn("[Settings] HuggingFaceToken decrypt failed, clearing");
                hfToken = "";
                App.Settings.Llama.HuggingFaceToken = "";
            }
        }
        
        if (!string.IsNullOrEmpty(hfToken))
        {
            HuggingFaceTokenBox.Password = hfToken;
        }

        // Hotkey
        HotkeyTriggerTextBox.Text = App.Settings.General.HotkeyTriggerKey;
        HotkeyDisplay.Text = App.Settings.General.HotkeyTriggerKey.ToUpper();
        HotkeyStatusText.Text = $"Ctrl+Shift+{App.Settings.General.HotkeyTriggerKey.ToUpper()}";
        
        // Update Quick Start hotkey display
        var quickStartHotkeyRun = FindName("QuickStartHotkeyRun") as System.Windows.Documents.Run;
        if (quickStartHotkeyRun != null)
        {
            quickStartHotkeyRun.Text = App.Settings.General.HotkeyTriggerKey.ToUpper();
        }
        
        // Theme
        ThemeComboBox.SelectedIndex = App.Theme.GetCurrentThemeIndex();

        // Launch at login
        LaunchAtLoginCheckbox.IsChecked = App.Settings.General.LaunchAtLogin;
        
        // Clipboard restore settings
        RestoreClipboardCheckbox.IsChecked = App.Settings.General.RestoreClipboard;
        ClipboardDelayTextBox.Text = App.Settings.General.ClipboardRestoreDelayMs.ToString();
        
        // Recording mode
        PushToTalkCheckbox.IsChecked = App.Settings.General.PushToTalkMode;
        
        // Mark as initialized - now TextChanged will save settings
        _isInitialized = true;
        LoggingService.Info("[UI] MainWindow initialized, _isInitialized = true");
    }

    public void NavigateToSettings()
    {
        MainTabControl.SelectedIndex = 1; // Audio tab (or could be Settings tab index)
    }

    public void UpdateLastTranscription(string text)
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] UpdateLastTranscription called with: '{text}'");
        Dispatcher.Invoke(() =>
        {
            // Don't switch tabs - stay on current tab
            
            if (string.IsNullOrEmpty(text))
            {
                LastTranscriptionText.Text = "No transcriptions yet";
            }
            else
            {
                LastTranscriptionText.Text = text;
            }
            System.Diagnostics.Debug.WriteLine($"[DEBUG] LastTranscriptionText.Text is now: '{LastTranscriptionText.Text}'");
        });
    }

    private async Task LoadHistoryAsync()
    {
        var records = await App.Database.GetTranscriptionsAsync();
        
        // Group by date and session
        var items = records.Select(r => new TranscriptionListItem
        {
            Id = r.Id,
            Text = r.Text,
            OriginalText = r.Text,
            Timestamp = r.Timestamp,
            Duration = r.Duration,
            DisplayTime = DateTime.Parse(r.Timestamp).ToString("h:mm tt"),
            DateHeader = GetDateHeader(DateTime.Parse(r.Timestamp)),
            SessionId = GetSessionId(DateTime.Parse(r.Timestamp))
        }).ToList();
        
        // Apply grouping
        var groupedItems = ApplyGrouping(items);
        
        HistoryListBox.ItemsSource = groupedItems;
        
        // Show/hide empty state based on item count
        UpdateEmptyState(items.Count);
    }
    
    private void UpdateEmptyState(int itemCount)
    {
        // Use the HistoryListBox to find the EmptyStatePanel in the visual tree
        var grid = HistoryListBox.Parent as Grid;
        if (grid != null)
        {
            foreach (var child in grid.Children)
            {
                if (child is System.Windows.Controls.StackPanel panel && panel.Name == "EmptyStatePanel")
                {
                    panel.Visibility = itemCount == 0 ? Visibility.Visible : Visibility.Collapsed;
                    break;
                }
            }
        }
    }
    
    private string GetDateHeader(DateTime dt)
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        
        if (dt.Date == today)
            return "Today — " + dt.ToString("MMMM d, yyyy");
        else if (dt.Date == yesterday)
            return "Yesterday — " + dt.ToString("MMMM d, yyyy");
        else if (dt.Date > today.AddDays(-7))
            return dt.ToString("dddd — MMMM d, yyyy");
        else
            return dt.ToString("MMMM d, yyyy");
    }
    
    private string GetSessionId(DateTime dt)
    {
        // Group entries within 5 minutes of each other as the same session
        return dt.ToString("yyyy-MM-dd-HH-mm");
    }
    
    private System.Collections.ObjectModel.Collection<TranscriptionListItem> ApplyGrouping(List<TranscriptionListItem> items)
    {
        // For now, just return a flat list with date headers
        // The ListBox.GroupStyle in XAML will handle visual grouping
        return new System.Collections.ObjectModel.Collection<TranscriptionListItem>(items);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedIndex = 1;
    }

    private async void PresetFastest_Click(object sender, RoutedEventArgs e)
    {
        var modelSize = "tiny";
        var modelFileName = $"ggml-{modelSize}.bin";
        var modelPath = System.IO.Path.Combine(App.WhisperModelsPath, modelFileName);
        
        // Check if model already exists
        if (!System.IO.File.Exists(modelPath) || new System.IO.FileInfo(modelPath).Length < 1024 * 1024)
        {
            // Model not found, ask to download
            var result = System.Windows.MessageBox.Show(
                "The Tiny model (~75MB) is not downloaded.\n\nWould you like to download it now?",
                "Download Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                // Download the model
                App.Settings.Whisper.ModelSize = modelSize;
                App.Settings.Save();
                UpdatePresetButtons(modelSize);
                
                await DownloadWhisperModelAsync(modelSize);
                return;
            }
        }
        else
        {
            // Model exists, update settings and load the model
            App.Settings.Whisper.ModelSize = modelSize;
            App.Settings.Whisper.ModelPath = modelPath;
            App.Settings.Save();
            
            // Reload the model
            App.Whisper.ReloadModel(modelPath);
            ModelPathText.Text = System.IO.Path.GetFileName(modelPath);
            WhisperStatusText.Text = "Ready";
            
            UpdatePresetButtons(modelSize);
        }
        
        LoggingService.Debug("[Presets] Applied Fastest preset - Tiny model");
    }

    private async void PresetBalanced_Click(object sender, RoutedEventArgs e)
    {
        var modelSize = "base";
        var modelFileName = $"ggml-{modelSize}.bin";
        var modelPath = System.IO.Path.Combine(App.WhisperModelsPath, modelFileName);
        
        // Check if model already exists
        if (!System.IO.File.Exists(modelPath) || new System.IO.FileInfo(modelPath).Length < 1024 * 1024)
        {
            // Model not found, ask to download
            var result = System.Windows.MessageBox.Show(
                "The Base model (~150MB) is not downloaded.\n\nWould you like to download it now?",
                "Download Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                // Download the model
                App.Settings.Whisper.ModelSize = modelSize;
                App.Settings.Save();
                UpdatePresetButtons(modelSize);
                
                await DownloadWhisperModelAsync(modelSize);
                return;
            }
        }
        else
        {
            // Model exists, update settings and load the model
            App.Settings.Whisper.ModelSize = modelSize;
            App.Settings.Whisper.ModelPath = modelPath;
            App.Settings.Save();
            
            // Reload the model
            App.Whisper.ReloadModel(modelPath);
            ModelPathText.Text = System.IO.Path.GetFileName(modelPath);
            WhisperStatusText.Text = "Ready";
            
            UpdatePresetButtons(modelSize);
        }
        
        LoggingService.Debug("[Presets] Applied Balanced preset - Base model");
    }

    private async void PresetAccurate_Click(object sender, RoutedEventArgs e)
    {
        var modelSize = "medium";
        var modelFileName = $"ggml-{modelSize}.bin";
        var modelPath = System.IO.Path.Combine(App.WhisperModelsPath, modelFileName);
        
        // Check if model already exists
        if (!System.IO.File.Exists(modelPath) || new System.IO.FileInfo(modelPath).Length < 1024 * 1024)
        {
            // Model not found, ask to download
            var result = System.Windows.MessageBox.Show(
                "The Medium model (~1.5GB) is not downloaded.\n\nWould you like to download it now?\n\nThis may take a while depending on your internet connection.",
                "Download Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                // Download the model
                App.Settings.Whisper.ModelSize = modelSize;
                App.Settings.Save();
                UpdatePresetButtons(modelSize);
                
                await DownloadWhisperModelAsync(modelSize);
                return;
            }
        }
        else
        {
            // Model exists, update settings and load the model
            App.Settings.Whisper.ModelSize = modelSize;
            App.Settings.Whisper.ModelPath = modelPath;
            App.Settings.Save();
            
            // Reload the model
            App.Whisper.ReloadModel(modelPath);
            ModelPathText.Text = System.IO.Path.GetFileName(modelPath);
            WhisperStatusText.Text = "Ready";
            
            UpdatePresetButtons(modelSize);
        }
        
        LoggingService.Debug("[Presets] Applied Most Accurate preset - Medium model");
    }

    private void UpdatePresetButtons(string selectedSize)
    {
        // Reset all buttons to secondary style
        PresetFastestBtn.Style = (Style)FindResource("SecondaryButtonStyle");
        PresetBalancedBtn.Style = (Style)FindResource("SecondaryButtonStyle");
        PresetAccurateBtn.Style = (Style)FindResource("SecondaryButtonStyle");
        
        // Update preview panel
        string presetName = "Custom";
        string modelName = selectedSize;
        string audioRate = "16kHz";
        string processing = "Standard";
        
        // Highlight the selected preset and update preview
        switch (selectedSize.ToLower())
        {
            case "tiny":
                PresetFastestBtn.Style = (Style)FindResource("PrimaryButtonStyle");
                presetName = "Fastest";
                modelName = "tiny (~75MB)";
                audioRate = "8kHz";
                processing = "Minimal";
                break;
            case "base":
                PresetBalancedBtn.Style = (Style)FindResource("PrimaryButtonStyle");
                presetName = "Balanced";
                modelName = "base (~150MB)";
                audioRate = "16kHz";
                processing = "Standard";
                break;
            case "small":
            case "medium":
            case "large":
                PresetAccurateBtn.Style = (Style)FindResource("PrimaryButtonStyle");
                presetName = "Most Accurate";
                modelName = selectedSize + " (~1.5GB+)";
                audioRate = "48kHz";
                processing = "Full";
                break;
        }
        
        // Update preview panel text
        Dispatcher.Invoke(() =>
        {
            CurrentPresetLabel.Text = $"Current: {presetName}";
            PreviewModelText.Text = modelName;
            PreviewAudioText.Text = audioRate;
            PreviewProcessingText.Text = processing;
        });
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        // Don't allow starting new recording while processing
        if (_isProcessing)
        {
            LoggingService.Debug("[DEBUG] RecordButton_Click ignored - already processing");
            return;
        }
        
        LoggingService.Debug($"[DEBUG] RecordButton_Click called. IsRecording={App.Audio.IsRecording}");
        if (App.Audio.IsRecording)
        {
            // Stop recording
            LoggingService.Debug("[DEBUG] Stopping recording via button click");
            _isProcessing = true;
            RecordButton.IsEnabled = false;
            RecordButton.Content = "Processing...";
            Task.Run(async () =>
            {
                var audioFile = await App.Audio.StopRecordingAsync();
                LoggingService.Debug($"[DEBUG] StopRecordingAsync returned: {audioFile}");
                if (!string.IsNullOrEmpty(audioFile))
                {
                    await ProcessTranscriptionAsync(audioFile);
                }
                Dispatcher.Invoke(() =>
                {
                    _isProcessing = false;
                    RecordButton.IsEnabled = true;
                    RecordButton.Content = "Start Recording";
                });
            });
        }
        else
        {
            // Start recording
            LoggingService.Debug("[DEBUG] Starting recording via button click");
            App.Audio.StartRecording();
        }
    }

    private async Task ProcessTranscriptionAsync(string audioFile)
    {
        App.IsTranscribing = true;
        
        try
        {
            LoggingService.Debug("[DEBUG] Starting Whisper transcription...");
            var text = await App.Whisper.TranscribeAsync(audioFile) ?? string.Empty;
            //LoggingService.Debug($"[DEBUG] Whisper transcription complete: {text}");
            LoggingService.Info("[Whisper] Whisper transcription complete: " + text);

            LoggingService.Debug($"[DEBUG] llama Enabled: {App.Settings.Llama.Enabled}, Llama Loaded: {App.Llama.IsLoaded}");

            if (App.Settings.Llama.Enabled && App.Llama.IsLoaded && !string.IsNullOrEmpty(text))
            {
                // Check if translation is enabled
                if (App.Settings.Llama.Translate)
                {
                    LoggingService.Debug("[DEBUG] Running Llama translation...");
                    var targetLang = App.Settings.Llama.TranslateTo ?? "en";
                    text = await App.Llama.TranslateTextAsync(text, targetLang);
                    LoggingService.Debug($"[LLAMA] Llama translation complete to {targetLang}: {text}");
                }
                else
                {
                    LoggingService.Debug("[DEBUG] Running Llama text formatting...");
                    text = await App.Llama.FormatTextAsync(text);
                    LoggingService.Debug($"[LLAMA] Llama formatting complete: {text}");
                }
            }

            // Clipboard operations must run on the UI thread (STA mode)
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var previousClipboard = App.Clipboard.GetText();
                    var previousWindow = App.Hotkey.GetPreviousWindow();
                    
                    if (string.IsNullOrEmpty(text))
                    {
                        return;
                    }
                    
                    // Set the transcribed text to clipboard
                    App.Clipboard.SetText(text);

                    // Paste to previous window (now uses retry mechanism internally)
                    await App.Clipboard.PasteToWindow(previousWindow);

                    // Restore previous clipboard after delay (if enabled)
                    if (App.Settings.General.RestoreClipboard && !string.IsNullOrEmpty(previousClipboard))
                    {
                        var delay = App.Settings.General.ClipboardRestoreDelayMs;
                        if (delay > 0)
                        {
                            await Task.Delay(delay);
                            App.Clipboard.SetText(previousClipboard);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Error(ex, "[CLIPBOARD] Error");
                }
            });

            await App.Database.AddTranscriptionAsync(text ?? string.Empty, App.Audio.LastRecordingDuration,
                App.Settings.Whisper.ModelPath, App.Settings.Whisper.Language);

            // UI updates must run on the UI thread
            await Dispatcher.InvokeAsync(async () =>
            {
                UpdateLastTranscription(text ?? string.Empty);
                await LoadHistoryAsync();
            });

            // Cleanup
            if (System.IO.File.Exists(audioFile))
            {
                System.IO.File.Delete(audioFile);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Transcription error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            App.IsTranscribing = false;
        }
    }

    private void MicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicrophoneComboBox.SelectedItem is ComboBoxItem item)
        {
            App.Settings.Audio.DeviceId = item.Tag?.ToString() ?? "";
            App.Settings.Save();
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem item)
        {
            App.Settings.Whisper.Language = item.Tag?.ToString() ?? "auto";
            App.Settings.Save();
        }
    }
    
    private void AutoDetectLanguageCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        var isAutoDetect = AutoDetectLanguageCheckbox.IsChecked ?? true;
        
        // Show/hide language dropdown based on auto-detect toggle
        if (LanguageDropdownPanel != null)
        {
            LanguageDropdownPanel.Visibility = isAutoDetect ? Visibility.Collapsed : Visibility.Visible;
        }
        
        // Update settings
        if (isAutoDetect)
        {
            App.Settings.Whisper.Language = "auto";
        }
        else if (LanguageComboBox.SelectedItem is ComboBoxItem item)
        {
            App.Settings.Whisper.Language = item.Tag?.ToString() ?? "en";
        }
        App.Settings.Save();
    }

    private void TranslateCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        App.Settings.Whisper.Translate = TranslateCheckbox.IsChecked ?? false;
        App.Settings.Save();
    }

    private void CustomVocabularyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        App.Settings.Whisper.CustomVocabulary = CustomVocabularyTextBox.Text;
        App.Settings.Save();
    }

    private void AddReplacement_Click(object sender, RoutedEventArgs e)
    {
        var source = NewReplacementSourceBox.Text.Trim();
        var replacement = NewReplacementTargetBox.Text.Trim();
        
        if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(replacement))
        {
            App.Settings.Whisper.WordReplacements.Add(new WordReplacement 
            { 
                Source = source, 
                Replacement = replacement 
            });
            App.Settings.Save();
            
            // Clear input boxes
            NewReplacementSourceBox.Text = "";
            NewReplacementTargetBox.Text = "";
            
            // Refresh the list
            ReplacementsListBox.ItemsSource = null;
            ReplacementsListBox.ItemsSource = App.Settings.Whisper.WordReplacements;
        }
    }

    private void RemoveReplacement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string source)
        {
            var itemToRemove = App.Settings.Whisper.WordReplacements
                .FirstOrDefault(wr => wr.Source == source);
            
            if (itemToRemove != null)
            {
                App.Settings.Whisper.WordReplacements.Remove(itemToRemove);
                App.Settings.Save();
                
                // Refresh the list
                ReplacementsListBox.ItemsSource = null;
                ReplacementsListBox.ItemsSource = App.Settings.Whisper.WordReplacements;
            }
        }
    }

    private void SelectModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Whisper Models (*.bin)|*.bin|All Files (*.*)|*.*",
            Title = "Select Whisper Model"
        };

        if (dialog.ShowDialog() == true)
        {
            App.Whisper.ReloadModel(dialog.FileName);
            ModelPathText.Text = System.IO.Path.GetFileName(dialog.FileName);
            WhisperStatusText.Text = "Ready";
            
            // Update model info
            UpdateModelInfo(dialog.FileName);
        }
    }
    
    private void ValidateModel_Click(object sender, RoutedEventArgs e)
    {
        // Validate the current model
        var modelPath = App.Settings.Whisper.ModelPath;
        if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
        {
            ShowModelValidation(false);
            return;
        }
        
        // Check file size
        var fileInfo = new System.IO.FileInfo(modelPath);
        var isValid = fileInfo.Exists && fileInfo.Length > 1024 * 1024; // At least 1MB
        
        ShowModelValidation(isValid);
    }
    
    private void UpdateModelInfo(string filePath)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            if (fileInfo.Exists)
            {
                // Show size badge
                var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
                ModelSizeText.Text = $"~{sizeMB:F0} MB";
                ModelSizeBadge.Visibility = Visibility.Visible;
                
                // Show sample rate badge (assume 16kHz for whisper)
                ModelSampleRateText.Text = "16kHz";
                ModelSampleRateBadge.Visibility = Visibility.Visible;
                
                // Auto-validate
                ShowModelValidation(sizeMB > 1);
            }
        }
        catch
        {
            ModelSizeBadge.Visibility = Visibility.Collapsed;
            ModelSampleRateBadge.Visibility = Visibility.Collapsed;
            ModelValidationBadge.Visibility = Visibility.Collapsed;
        }
    }
    
    private void ShowModelValidation(bool isValid)
    {
        ModelValidationBadge.Visibility = Visibility.Visible;
        if (isValid)
        {
            ModelValidationBadge.SetResourceReference(Border.BackgroundProperty, "ValidBadgeBrush");
            ModelValidationText.Text = "Valid";
        }
        else
        {
            ModelValidationBadge.SetResourceReference(Border.BackgroundProperty, "InvalidBadgeBrush");
            ModelValidationText.Text = "Invalid";
        }
    }

    private async void DownloadTiny_Click(object sender, RoutedEventArgs e) => await DownloadWhisperModelAsync("tiny");
    private async void DownloadBase_Click(object sender, RoutedEventArgs e) => await DownloadWhisperModelAsync("base");
    private async void DownloadSmall_Click(object sender, RoutedEventArgs e) => await DownloadWhisperModelAsync("small");
    private async void DownloadMedium_Click(object sender, RoutedEventArgs e) => await DownloadWhisperModelAsync("medium");
    private async void DownloadLarge_Click(object sender, RoutedEventArgs e) => await DownloadWhisperModelAsync("large");

    private void BrowseDownloadPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder for Whisper model downloads",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        // Set initial directory to saved path, then default
        var savedPath = App.Settings.General.ModelDownloadPath;
        if (!string.IsNullOrEmpty(savedPath))
        {
            dialog.SelectedPath = savedPath;
        }
        else if (!string.IsNullOrEmpty(App.WhisperModelsPath))
        {
            dialog.SelectedPath = App.WhisperModelsPath;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ModelDownloadPathTextBox.Text = dialog.SelectedPath;
            App.Settings.General.ModelDownloadPath = dialog.SelectedPath;
            App.Settings.Save();
            DownloadPathSummaryText.Text = $"Using: {dialog.SelectedPath}";
            WhisperStatusText.Text = "Download path changed - restart to apply";
        }
    }

    private void BrowseLlamaDownloadPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder for GGUF model downloads",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        // Set initial directory to saved path, then default
        var savedPath = App.Settings.General.LlamaDownloadPath;
        if (!string.IsNullOrEmpty(savedPath))
        {
            dialog.SelectedPath = savedPath;
        }
        else if (!string.IsNullOrEmpty(App.LlamaModelsPath))
        {
            dialog.SelectedPath = App.LlamaModelsPath;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            LlamaDownloadPathTextBox.Text = dialog.SelectedPath;
            App.Settings.General.LlamaDownloadPath = dialog.SelectedPath;
            App.Settings.Save();
            LlamaDownloadPathSummaryText.Text = $"Using: {dialog.SelectedPath}";
            LlamaStatusText.Text = "Download path changed - restart to apply";
        }
    }

    private void LlamaDownloadPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // When path changes, update status text
        var newPath = LlamaDownloadPathTextBox.Text;
        if (!string.IsNullOrEmpty(newPath) && System.IO.Directory.Exists(newPath))
        {
            LlamaDownloadPathSummaryText.Text = $"Using: {newPath}";
            LlamaPathValidIcon.Visibility = Visibility.Visible;
            
            // Count GGUF model files
            var modelCount = System.IO.Directory.GetFiles(newPath, "*.gguf").Length;
            if (modelCount > 0)
            {
                LlamaModelCountText.Text = $"{modelCount} model{(modelCount != 1 ? "s" : "")}";
                LlamaModelCountBadge.Visibility = Visibility.Visible;
            }
            else
            {
                LlamaModelCountBadge.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            LlamaPathValidIcon.Visibility = Visibility.Collapsed;
            LlamaModelCountBadge.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(newPath))
            {
                LlamaDownloadPathSummaryText.Text = $"New path will be created: {newPath}";
            }
            else
            {
                LlamaDownloadPathSummaryText.Text = $"Default: {App.LlamaModelsPath}";
            }
        }
    }

    private void ModelDownloadPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // When path changes, re-enable all download buttons to allow checking/updating
        DownloadTinyBtn.Content = "Download";
        DownloadTinyBtn.IsEnabled = true;
        DownloadBaseBtn.Content = "Download";
        DownloadBaseBtn.IsEnabled = true;
        DownloadSmallBtn.Content = "Download";
        DownloadSmallBtn.IsEnabled = true;
        DownloadMediumBtn.Content = "Download";
        DownloadMediumBtn.IsEnabled = true;
        DownloadLargeBtn.Content = "Download";
        DownloadLargeBtn.IsEnabled = true;

        // Update validation icon and model count
        var newPath = ModelDownloadPathTextBox.Text;
        if (!string.IsNullOrEmpty(newPath) && System.IO.Directory.Exists(newPath))
        {
            DownloadPathSummaryText.Text = $"Using: {newPath}";
            WhisperPathValidIcon.Visibility = Visibility.Visible;
            
            // Count model files
            var modelCount = System.IO.Directory.GetFiles(newPath, "*.bin").Length;
            if (modelCount > 0)
            {
                WhisperModelCountText.Text = $"{modelCount} model{(modelCount != 1 ? "s" : "")}";
                WhisperModelCountBadge.Visibility = Visibility.Visible;
            }
            else
            {
                WhisperModelCountBadge.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            WhisperPathValidIcon.Visibility = Visibility.Collapsed;
            WhisperModelCountBadge.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(newPath))
            {
                DownloadPathSummaryText.Text = $"New path will be created: {newPath}";
            }
        }
    }

    private async Task DownloadWhisperModelAsync(string size)
    {
        // Disable button during download
        var buttonName = $"Download{char.ToUpper(size[0])}{size.Substring(1)}Btn";
        var downloadButton = this.FindName(buttonName) as System.Windows.Controls.Button;
        if (downloadButton != null)
        {
            downloadButton.IsEnabled = false;
            downloadButton.Content = "Downloading...";
        }
        
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadStatusText.Visibility = Visibility.Visible;
        var displayFileName = size.ToLowerInvariant() == "large" ? "ggml-large-v3.bin" : $"ggml-{size}.bin";
        DownloadStatusText.Text = $"Downloading {displayFileName}...";
        DownloadProgressBar.Value = 0;

        var progress = new Progress<double>(p =>
        {
            DownloadProgressBar.Value = p;
            var displayFileName = size.ToLowerInvariant() == "large" ? "ggml-large-v3.bin" : $"ggml-{size}.bin";
            DownloadStatusText.Text = $"Downloading {displayFileName}... {p:F0}%";
        });

        var path = await App.ModelDownload.DownloadWhisperModelAsync(size, progress);

        if (!string.IsNullOrEmpty(path))
        {
            DownloadStatusText.Text = $"Downloaded {System.IO.Path.GetFileName(path)} successfully!";
            App.Whisper.ReloadModel(path);
            ModelPathText.Text = System.IO.Path.GetFileName(path);
            WhisperStatusText.Text = "Ready";
            
            // Update model info badges
            UpdateModelInfo(path);

            // Update button to show "Re-download" so user can re-download if needed
            if (downloadButton != null)
            {
                downloadButton.Content = "Re-download";
                downloadButton.IsEnabled = true;
            }
        }
        else
        {
            DownloadStatusText.Text = "Download failed";
            if (downloadButton != null)
            {
                downloadButton.Content = "Download";
                downloadButton.IsEnabled = true;
            }
        }

        await Task.Delay(2000);
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        DownloadStatusText.Visibility = Visibility.Collapsed;
    }

    private void LlamaEnableCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        App.Settings.Llama.Enabled = LlamaEnableCheckbox.IsChecked ?? false;
        App.Settings.Save();

        if (App.Settings.Llama.Enabled && !string.IsNullOrEmpty(App.Settings.Llama.ModelPath))
        {
            Task.Run(async () => await App.Llama.InitializeAsync(App.Settings.Llama.ModelPath));
        }
        else
        {
            App.Llama.Unload();
        }
    }

    private void LlamaTranslateCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        var isEnabled = LlamaTranslateCheckbox.IsChecked ?? false;
        App.Settings.Llama.Translate = isEnabled;
        App.Settings.Save();
        
        // Disable dependent controls when toggle is off
        LlamaTranslationOptionsPanel.IsEnabled = isEnabled;
        LlamaTargetLanguageComboBox.IsEnabled = isEnabled;
        
        // Visual feedback - dim the dependent controls when disabled
        if (isEnabled)
        {
            LlamaTranslationOptionsPanel.Opacity = 1.0;
            LlamaTranslationDescription.Opacity = 1.0;
        }
        else
        {
            LlamaTranslationOptionsPanel.Opacity = 0.4;
            LlamaTranslationDescription.Opacity = 0.4;
        }
        
        LoggingService.Debug($"[LLAMA] Translation enabled: {App.Settings.Llama.Translate}");
    }

    private void LlamaTargetLanguageComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LlamaTargetLanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
        {
            App.Settings.Llama.TranslateTo = langCode;
            App.Settings.Save();
            LoggingService.Debug($"[LLAMA] Translation target language set to: {langCode}");
        }
    }

    private void SelectLlamaModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "GGUF Models (*.gguf)|*.gguf|All Files (*.*)|*.*",
            Title = "Select Llama Model"
        };

        if (dialog.ShowDialog() == true)
        {
            App.Settings.Llama.ModelPath = dialog.FileName;
            App.Settings.Save();
            LlamaModelPathText.Text = System.IO.Path.GetFileName(dialog.FileName);
            UpdateLlamaModelBadges(dialog.FileName);

            // Reinitialize Llama with the new model
            if (App.Settings.Llama.Enabled && !string.IsNullOrEmpty(dialog.FileName))
            {
                LlamaStatusText.Text = "Loading model...";
                Task.Run(async () =>
                {
                    await App.Llama.InitializeAsync(dialog.FileName);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LlamaStatusText.Text = App.Llama.IsLoaded 
                            ? $"Model loaded: {System.IO.Path.GetFileName(dialog.FileName)}"
                            : "Failed to load model";
                    });
                });
            }
        }
    }

    private void BrowseHuggingFaceLlama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://huggingface.co/models?num_parameters=min:0,max:6B&library=gguf&apps=llama.cpp&sort=likes",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[ERROR] Could not open URL");
        }
    }

    private async void DownloadLlamaModel_Click(object sender, RoutedEventArgs e)
    {
        var modelId = HuggingFaceModelIdTextBox.Text.Trim();
        if (string.IsNullOrEmpty(modelId))
        {
            System.Windows.MessageBox.Show("Please enter a model ID", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Save the model ID to settings
        App.Settings.Llama.ModelId = modelId;
        App.Settings.Save();

        LoggingService.Info("Model ID: " + modelId);
        LoggingService.Info("Download path: " + App.Settings.General.LlamaDownloadPath);
        LoggingService.Info("LlamaModelsPath: " + App.LlamaModelsPath);

        // Use Llama-specific progress bar
        LlamaDownloadProgressBar.Visibility = Visibility.Visible;
        LlamaDownloadStatusText.Visibility = Visibility.Visible;
        LlamaDownloadStatusText.Text = "Searching for model...";
        LlamaDownloadProgressBar.Value = 0;

        var progress = new Progress<double>(p =>
        {
            LlamaDownloadProgressBar.Value = p;
            LlamaDownloadStatusText.Text = $"Downloading... {p:F0}%";
        });

        var path = await App.ModelDownload.DownloadLlamaModelAsync(modelId, progress);

        if (!string.IsNullOrEmpty(path))
        {
            if (path.StartsWith("ERROR:"))
            {
                System.Windows.MessageBox.Show(path.Substring(6), "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                LlamaDownloadStatusText.Text = "Download failed";
                await Task.Delay(2000);
                LlamaDownloadProgressBar.Visibility = Visibility.Collapsed;
                LlamaDownloadStatusText.Visibility = Visibility.Collapsed;
                return;
            }

            LlamaDownloadStatusText.Text = $"Downloaded {System.IO.Path.GetFileName(path)} successfully!";
            App.Settings.Llama.ModelPath = path;
            App.Settings.Save();
            LlamaModelPathText.Text = System.IO.Path.GetFileName(path);
            UpdateLlamaModelBadges(path);

            if (App.Settings.Llama.Enabled)
            {
                await App.Llama.InitializeAsync(path);
            }
        }
        else
        {
            LlamaDownloadStatusText.Text = "Could not find a GGUF file for this model";
            System.Windows.MessageBox.Show("Could not find a GGUF file for this model.\n\nMake sure the model ID is correct and the model has GGUF files available.", "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await Task.Delay(2000);
        LlamaDownloadProgressBar.Visibility = Visibility.Collapsed;
        LlamaDownloadStatusText.Visibility = Visibility.Collapsed;
    }

    private void HuggingFaceTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Don't save during initialization or loading
        if (!_isInitialized || _isLoading) 
        {
            LoggingService.Debug($"[UI] HuggingFaceTokenBox_PasswordChanged: _isInitialized={_isInitialized}, _isLoading={_isLoading}, skipping save");
            return;
        }
        
        LoggingService.Debug($"[UI] HuggingFaceTokenBox_PasswordChanged: Saving token, length={HuggingFaceTokenBox.Password?.Length ?? 0}");
        
        // Sync with TextBox if visible
        if (HuggingFaceTokenTextBox.Visibility == Visibility.Visible)
        {
            HuggingFaceTokenTextBox.Text = HuggingFaceTokenBox.Password;
        }
        
        App.Settings.Llama.HuggingFaceToken = HuggingFaceTokenBox.Password ?? string.Empty;
        App.Settings.Save();
    }
    
    private void HuggingFaceTokenTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Don't save during initialization or loading
        if (!_isInitialized || _isLoading) return;
        
        // Sync with PasswordBox
        HuggingFaceTokenBox.Password = HuggingFaceTokenTextBox.Text;
        
        App.Settings.Llama.HuggingFaceToken = HuggingFaceTokenTextBox.Text ?? string.Empty;
        App.Settings.Save();
    }
    
    private void ToggleHuggingFaceToken_Click(object sender, RoutedEventArgs e)
    {
        if (HuggingFaceTokenBox.Visibility == Visibility.Visible)
        {
            // Show token
            HuggingFaceTokenTextBox.Text = HuggingFaceTokenBox.Password;
            HuggingFaceTokenBox.Visibility = Visibility.Collapsed;
            HuggingFaceTokenTextBox.Visibility = Visibility.Visible;
            ToggleHuggingFaceTokenBtn.Content = "🔒";
            ToggleHuggingFaceTokenBtn.ToolTip = "Hide token";
        }
        else
        {
            // Hide token
            HuggingFaceTokenBox.Password = HuggingFaceTokenTextBox.Text;
            HuggingFaceTokenTextBox.Visibility = Visibility.Collapsed;
            HuggingFaceTokenBox.Visibility = Visibility.Visible;
            ToggleHuggingFaceTokenBtn.Content = "👁";
            ToggleHuggingFaceTokenBtn.ToolTip = "Show token";
        }
    }

    private bool _isInitialized = false;
    private bool _isLoading = false;
    
    private void HuggingFaceModelIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Validate the input format (should be "username/model-name")
        var input = HuggingFaceModelIdTextBox.Text?.Trim() ?? "";
        
        // Check if it's a valid HuggingFace model ID format (allows letters, numbers, dashes, dots, underscores)
        bool isValid = System.Text.RegularExpressions.Regex.IsMatch(input, @"^[\w.-]+/[\w.-]+$");
        
        // Update validation icon
        if (string.IsNullOrEmpty(input))
        {
            ValidationIcon.Text = "";
            ValidationIcon.Foreground = null;
        }
        else if (isValid)
        {
            ValidationIcon.Text = "✓";
            ValidationIcon.Foreground = System.Windows.Media.Brushes.LimeGreen;
        }
        else
        {
            ValidationIcon.Text = "⚠";
            ValidationIcon.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
        
        // Don't save during initialization
        if (!_isInitialized) return;
        
        App.Settings.Llama.ModelId = input;
        App.Settings.Save();
        LoggingService.Debug($"[UI] Saved ModelId: {input}");
    }

    private void HotkeyTriggerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = HotkeyTriggerTextBox.Text;
        if (text.Length == 1 && char.IsLetterOrDigit(text[0]))
        {
            App.Hotkey.SetTriggerKey(text.ToLower());
            HotkeyDisplay.Text = text.ToUpper();
            HotkeyStatusText.Text = $"Ctrl+Shift+{text.ToUpper()}";
            
            // Update Quick Start hotkey display
            var quickStartHotkeyRun = FindName("QuickStartHotkeyRun") as System.Windows.Documents.Run;
            if (quickStartHotkeyRun != null)
            {
                quickStartHotkeyRun.Text = text.ToUpper();
            }
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedIndex >= 0)
        {
            string[] themes = { "Light", "Dark", "Nord", "Dracula", "Gruvbox", "Monokai", "Synthwave" };
            var theme = themes[ThemeComboBox.SelectedIndex];
            App.Theme.ApplyTheme(theme);
            App.Settings.General.Theme = theme;
            App.Settings.Save();
            
            // Force style refresh on preset buttons
            RefreshButtonStyles();
            
            LoggingService.Info($"[UI] Theme changed to: {theme}");
        }
    }
    
    private void RefreshButtonStyles()
    {
        // Re-fetch the style from resources (will get the new theme's style)
        var style = FindResource("SecondaryButtonStyle") as System.Windows.Style;
        if (style != null)
        {
            PresetFastestBtn.Style = style;
            PresetBalancedBtn.Style = style;
            PresetAccurateBtn.Style = style;
        }
        
        // Also update the selected preset button to show as selected
        if (App.Settings?.Whisper?.ModelSize != null)
        {
            UpdatePresetButtons(App.Settings.Whisper.ModelSize);
        }
    }

    private void LaunchAtLoginCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        var isEnabled = LaunchAtLoginCheckbox.IsChecked ?? false;
        App.Settings.General.LaunchAtLogin = isEnabled;
        App.Settings.Save();
        
        // Update Windows startup registration
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (isEnabled)
                {
                    // Add to startup
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("whisperMeOff", $"\"{exePath}\"");
                        LoggingService.Info("[Startup] Registered for launch at login");
                    }
                }
                else
                {
                    // Remove from startup
                    key.DeleteValue("whisperMeOff", false);
                    LoggingService.Info("[Startup] Unregistered from launch at login");
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "[Startup] Error");
        }
    }

    private void MinimizeToTrayCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        // XAML control not yet available - needs rebuild
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && App.Settings.General.MinimizeToTray)
        {
            Hide();
        }
    }

    private void RestoreClipboardCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        App.Settings.General.RestoreClipboard = RestoreClipboardCheckbox.IsChecked ?? false;
        App.Settings.Save();
    }

    private void ClipboardDelayTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(ClipboardDelayTextBox.Text, out int delay) && delay > 0)
        {
            App.Settings.General.ClipboardRestoreDelayMs = delay;
            App.Settings.Save();
        }
    }

    private void PushToTalkCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        App.Settings.General.PushToTalkMode = PushToTalkCheckbox.IsChecked ?? true;
        App.Settings.Save();
    }

    private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Live filtering - no need to press Enter
        var query = SearchTextBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            await LoadHistoryAsync();
        }
        else
        {
            var records = await App.Database.SearchTranscriptionsAsync(query);
            var items = records.Select(r => new TranscriptionListItem
            {
                Id = r.Id,
                Text = r.Text,
                OriginalText = r.Text,
                Timestamp = r.Timestamp,
                Duration = r.Duration,
                DisplayTime = DateTime.Parse(r.Timestamp).ToString("h:mm tt"),
                DateHeader = GetDateHeader(DateTime.Parse(r.Timestamp)),
                SessionId = GetSessionId(DateTime.Parse(r.Timestamp))
            }).ToList();

            HistoryListBox.ItemsSource = items;
            
            // Show/hide empty state based on item count
            UpdateEmptyState(items.Count);
        }
    }

    private void CopyTranscription_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is long id)
        {
            Task.Run(async () =>
            {
                var record = await App.Database.GetTranscriptionAsync(id);
                if (record != null)
                {
                    // Must run clipboard on UI thread
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        App.Clipboard.SetText(record.Text);
                    });
                }
            });
        }
    }

    private async void DeleteTranscription_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is long id)
        {
            await App.Database.DeleteTranscriptionAsync(id);
            await LoadHistoryAsync();
        }
    }

    private async void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Delete all transcriptions? This cannot be undone.",
            "Confirm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await App.Database.ClearAllTranscriptionsAsync();
            await LoadHistoryAsync();
        }
    }

    private async void ClearOlderThanButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItem = ClearOlderThanComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
        if (selectedItem == null) return;

        var hoursText = selectedItem.Tag?.ToString();
        if (!int.TryParse(hoursText, out int hours)) return;

        var olderThan = TimeSpan.FromHours(hours);
        var result = System.Windows.MessageBox.Show(
            $"Delete transcriptions older than {selectedItem.Content}? This cannot be undone.",
            "Confirm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            int deletedCount = await App.Database.ClearTranscriptionsOlderThanAsync(olderThan);
            await LoadHistoryAsync();
            System.Windows.MessageBox.Show(
                $"Deleted {deletedCount} transcriptions.",
                "Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
    
    private void ShowMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is long id)
        {
            var item = HistoryListBox.Items.OfType<TranscriptionListItem>()
                .FirstOrDefault(i => i.Id == id);
            
            if (item != null)
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }
    }
    
    private void CardBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Handle click on card to start editing
        if (sender is System.Windows.Controls.Border border && border.DataContext is TranscriptionListItem item)
        {
            // Don't start editing if clicking on buttons or checkboxes
            if (e.OriginalSource is System.Windows.Controls.Button || 
                e.OriginalSource is System.Windows.Controls.CheckBox)
                return;
            
            // Toggle edit mode on click
            item.IsEditing = true;
        }
    }
    
    private async void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Save changes when focus is lost
        if (sender is System.Windows.Controls.TextBox textBox && textBox.DataContext is TranscriptionListItem item)
        {
            if (item.Text != item.OriginalText)
            {
                await App.Database.UpdateTranscriptionAsync(item.Id, item.Text);
                item.OriginalText = item.Text;
            }
            item.IsEditing = false;
        }
    }
    
    private async void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            // Save on Enter key
            if (sender is System.Windows.Controls.TextBox textBox && textBox.DataContext is TranscriptionListItem item)
            {
                if (item.Text != item.OriginalText)
                {
                    await App.Database.UpdateTranscriptionAsync(item.Id, item.Text);
                    item.OriginalText = item.Text;
                }
                item.IsEditing = false;
                e.Handled = true;
            }
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Cancel editing on Escape
            if (sender is System.Windows.Controls.TextBox textBox && textBox.DataContext is TranscriptionListItem item)
            {
                item.Text = item.OriginalText; // Revert changes
                item.IsEditing = false;
                e.Handled = true;
            }
        }
    }

    #region Transformation Tab Event Handlers

    private TransformationType _currentTransformationType = TransformationType.Grammar;
    private TransformationDirection _currentDirection = TransformationDirection.Default;

    private void TransformationTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransformationTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentTransformationType = Enum.Parse<TransformationType>(tag);
            
            // Show/hide target language for translation
            TargetLanguagePanel.Visibility = _currentTransformationType == TransformationType.Translation 
                ? Visibility.Visible : Visibility.Collapsed;
            
            // Update direction ComboBox based on transformation type
            UpdateDirectionComboBox();
        }
    }

    private void UpdateDirectionComboBox()
    {
        TransformationDirectionComboBox.Items.Clear();
        
        var directions = _currentTransformationType switch
        {
            TransformationType.Tone => new[] { "Default", "Formal", "Informal" },
            TransformationType.Voice => new[] { "Default", "Active", "Passive" },
            TransformationType.Complexity => new[] { "Default", "Simplify", "Elaborate" },
            TransformationType.Professionalism => new[] { "Default", "Professional", "Casual" },
            TransformationType.Grammar => new[] { "Default" },
            TransformationType.Translation => new[] { "Default" },
            TransformationType.PersonalStyle => new[] { "Default" },
            TransformationType.Custom => new[] { "Default" },
            _ => new[] { "Default" }
        };
        
        foreach (var dir in directions)
        {
            TransformationDirectionComboBox.Items.Add(new ComboBoxItem { Content = dir, Tag = dir });
        }
        
        TransformationDirectionComboBox.SelectedIndex = 0;
    }

    private void TransformationDirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransformationDirectionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentDirection = Enum.Parse<TransformationDirection>(tag);
        }
    }

    private async void TransformButton_Click(object sender, RoutedEventArgs e)
    {
        LoggingService.Debug("[DEBUG] TransformButton_Click called");
        var inputText = TransformInputTextBox.Text?.Trim();
        LoggingService.Debug($"[DEBUG] Input text length: {inputText?.Length ?? 0}");
        
        if (string.IsNullOrEmpty(inputText))
        {
            TransformStatusText.Text = "Please enter text to transform.";
            LoggingService.Debug("[DEBUG] Empty input text");
            return;
        }

        LoggingService.Debug($"[DEBUG] Llama.IsLoaded: {App.Llama.IsLoaded}");
        
        if (!App.Llama.IsLoaded)
        {
            TransformStatusText.Text = "Llama model is not loaded. Please enable and load a Llama model in the Llama settings tab.";
            LoggingService.Debug("[DEBUG] Llama not loaded");
            return;
        }

        try
        {
            TransformButton.IsEnabled = false;
            TransformStatusText.Text = "Transforming...";
            
            // Initialize transformation service if needed
            await App.Transform.InitializeAsync();
            
            var request = new TransformationRequest
            {
                Text = inputText,
                TransformationType = _currentTransformationType,
                Direction = _currentDirection,
                PreserveProperNouns = PreserveProperNounsCheckBox.IsChecked ?? true,
                PreserveTechnicalTerms = PreserveTechnicalTermsCheckBox.IsChecked ?? true,
                MinQualityThreshold = (int)QualityThresholdSlider.Value,
                IncludeQualityMetrics = true
            };
            
            // Add target language for translation
            if (_currentTransformationType == TransformationType.Translation && 
                TransformTargetLanguageComboBox.SelectedItem is ComboBoxItem langItem)
            {
                request.TargetLanguage = langItem.Tag?.ToString();
            }
            
            var result = await App.Transform.TransformAsync(request);
            LoggingService.Debug($"[DEBUG] TransformAsync completed. Success: {result.Success}, Error: {result.ErrorMessage}");
            
            if (result.Success)
            {
                TransformOutputTextBox.Text = result.TransformedText;
                
                // Show quality metrics
                if (result.QualityMetrics != null)
                {
                    QualityMetricsPanel.Visibility = Visibility.Visible;
                    SimilarityProgressBar.Value = result.QualityMetrics.SimilarityScore;
                    SimilarityText.Text = $"{result.QualityMetrics.SimilarityScore:F0}%";
                    
                    ConfidenceProgressBar.Value = result.QualityMetrics.ConfidenceScore;
                    ConfidenceText.Text = $"{result.QualityMetrics.ConfidenceScore:F0}%";
                    
                    ReadabilityProgressBar.Value = result.QualityMetrics.ReadabilityScore;
                    ReadabilityText.Text = $"{result.QualityMetrics.ReadabilityScore:F0}%";
                    
                    OverallProgressBar.Value = result.QualityMetrics.OverallScore;
                    OverallScoreText.Text = $"{result.QualityMetrics.OverallScore:F0}%";
                }
                
                TransformStatusText.Text = $"Transformation completed in {result.ProcessingTimeMs}ms";
            }
            else
            {
                TransformStatusText.Text = $"Error: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Text transformation failed");
            LoggingService.Error($"[DEBUG] Stack trace: {ex.StackTrace}");
            TransformStatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            TransformButton.IsEnabled = true;
        }
    }

    private void ClearTransformButton_Click(object sender, RoutedEventArgs e)
    {
        TransformInputTextBox.Text = string.Empty;
        TransformOutputTextBox.Text = string.Empty;
        QualityMetricsPanel.Visibility = Visibility.Collapsed;
        TransformStatusText.Text = string.Empty;
    }

    private void PasteToTransformButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Clipboard.ContainsText())
        {
            TransformInputTextBox.Text = System.Windows.Clipboard.GetText();
        }
    }

    private void CopyTransformedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TransformOutputTextBox.Text))
        {
            System.Windows.Clipboard.SetText(TransformOutputTextBox.Text);
            TransformStatusText.Text = "Copied to clipboard.";
        }
    }

    private void ApplyToOutputButton_Click(object sender, RoutedEventArgs e)
    {
        TransformStatusText.Text = "Transformed text ready. Use Copy to copy it.";
    }

    private async void TransformationProfilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Profile selection changed
    }

    private async void LoadProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string profileId)
        {
            try
            {
                var profile = await App.Transform.GetProfileAsync(profileId);
                if (profile != null)
                {
                    // Set transformation type
                    _currentTransformationType = profile.TransformationType;
                    foreach (ComboBoxItem item in TransformationTypeComboBox.Items)
                    {
                        if (item.Tag?.ToString() == profile.TransformationType.ToString())
                        {
                            TransformationTypeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                    
                    // Set direction
                    _currentDirection = profile.Direction;
                    foreach (ComboBoxItem item in TransformationDirectionComboBox.Items)
                    {
                        if (item.Tag?.ToString() == profile.Direction.ToString())
                        {
                            TransformationDirectionComboBox.SelectedItem = item;
                            break;
                        }
                    }
                    
                    // Set options
                    PreserveProperNounsCheckBox.IsChecked = profile.PreserveProperNouns;
                    PreserveTechnicalTermsCheckBox.IsChecked = profile.PreserveTechnicalTerms;
                    QualityThresholdSlider.Value = profile.MinQualityThreshold;
                    
                    TransformStatusText.Text = $"Loaded profile: {profile.Name}";
                }
            }
            catch (Exception ex)
            {
                TransformStatusText.Text = $"Error loading profile: {ex.Message}";
            }
        }
    }

    private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string profileId)
        {
            var result = System.Windows.MessageBox.Show(
                "Delete this profile? This cannot be undone.",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await App.Transform.DeleteProfileAsync(profileId);
                    await LoadTransformationProfilesAsync();
                    TransformStatusText.Text = "Profile deleted.";
                }
                catch (Exception ex)
                {
                    TransformStatusText.Text = $"Error deleting profile: {ex.Message}";
                }
            }
        }
    }

    private async void CreateProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NewProfileNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            TransformStatusText.Text = "Please enter a profile name.";
            return;
        }
        
        try
        {
            var profile = new TransformationProfile
            {
                Name = name,
                Description = NewProfileDescriptionBox.Text?.Trim(),
                TransformationType = _currentTransformationType,
                Direction = _currentDirection,
                PreserveProperNouns = PreserveProperNounsCheckBox.IsChecked ?? true,
                PreserveTechnicalTerms = PreserveTechnicalTermsCheckBox.IsChecked ?? true,
                MinQualityThreshold = (int)QualityThresholdSlider.Value
            };
            
            await App.Transform.CreateProfileAsync(profile);
            
            NewProfileNameBox.Text = string.Empty;
            NewProfileDescriptionBox.Text = string.Empty;
            
            await LoadTransformationProfilesAsync();
            TransformStatusText.Text = $"Profile '{name}' created.";
        }
        catch (Exception ex)
        {
            TransformStatusText.Text = $"Error creating profile: {ex.Message}";
        }
    }

    private async Task LoadTransformationProfilesAsync()
    {
        try
        {
            if (TransformationProfilesListBox == null) return;
            var profiles = await App.Transform.GetProfilesAsync();
            TransformationProfilesListBox.ItemsSource = profiles.ToList();
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to load transformation profiles");
        }
    }

    #endregion
}

public class TranscriptionListItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isEditing;
    private bool _isExpanded;
    private string _text = "";
    private string _displayTime = "";
    
    public long Id { get; set; }
    public string OriginalText { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string DateHeader { get; set; } = "";
    public string SessionId { get; set; } = "";
    public double? Duration { get; set; }
    public int WordCount => string.IsNullOrWhiteSpace(Text) ? 0 : Text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    
    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(ShowExpandButton));
            OnPropertyChanged(nameof(ExpandButtonText));
            OnPropertyChanged(nameof(WordCount));
            OnPropertyChanged(nameof(DurationBadge));
        }
    }
    
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }
    
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); }
    }
    
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(ShowExpandButton));
            OnPropertyChanged(nameof(ExpandButtonText));
        }
    }
    
    // Show full text or truncated based on expansion state
    public string DisplayText
    {
        get
        {
            if (IsExpanded || Text.Length <= 200)
                return Text;
            
            // Truncate to ~200 chars but preserve word boundaries
            if (Text.Length <= 200)
                return Text;
            
            var truncated = Text.Substring(0, 200);
            var lastSpace = truncated.LastIndexOf(' ');
            if (lastSpace > 150)
                truncated = truncated.Substring(0, lastSpace);
            
            return truncated + "...";
        }
    }
    
    // Show expand button only if text is long enough
    public bool ShowExpandButton => Text.Length > 200;
    
    // Toggle button text
    public string ExpandButtonText => IsExpanded ? "Show less" : "Show more";
    
    // Duration badge text
    public string DurationBadge
    {
        get
        {
            if (Duration.HasValue && Duration.Value > 0)
            {
                if (Duration.Value < 60)
                    return $"~{(int)Duration.Value} sec";
                else
                    return $"~{Duration.Value / 60:F1} min";
            }
            return $"~{WordCount} words";
        }
    }
    
    // Display time (just the time, not full date)
    public string DisplayTime
    {
        get => _displayTime;
        set { _displayTime = value; OnPropertyChanged(nameof(DisplayTime)); }
    }
    
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
