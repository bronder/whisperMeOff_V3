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
    private CancellationTokenSource? _transcriptionCts;
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
        // Stop pre-recording buffer
        App.Audio.StopPreBuffer();
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
        
        // Load custom transformation prompts to UI
        LoadTransformPromptsToUI();
        
        // Start pre-recording buffer (300ms buffer before trigger) if enabled
        if (App.Settings.General.PreRecordingBuffer)
        {
            App.Audio.StartPreBuffer();
        }
        
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

        // Subscribe to Whisper model load/unload events
        App.Whisper.ModelLoaded += (s, isLoaded) => Dispatcher.Invoke(() =>
        {
            if (WhisperModelText != null)
            {
                var modelName = App.Whisper.LoadedModelName;
                WhisperModelText.Text = string.IsNullOrEmpty(modelName) ? "" : $" — {modelName}";
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
            MicrophoneComboBox.Items.Add(new ComboBoxItem { 
                Content = device.Name, 
                Tag = device.Id,
                ToolTip = device.FormatInfo // Store format info in tooltip
            });
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
        
        // Update device info display
        UpdateMicrophoneDeviceInfo();
    }

    private void RefreshMicrophones_Click(object sender, RoutedEventArgs e)
    {
        LoadAudioDevices();
        WhisperStatusText.Text = "Microphone list refreshed";
    }

    private void UpdateMicrophoneDeviceInfo()
    {
        if (MicrophoneComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var deviceId = selectedItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(deviceId))
            {
                var devices = App.Audio.GetAvailableDevices();
                var device = devices.FirstOrDefault(d => d.Id == deviceId);
                if (device != null)
                {
                    MicrophoneDeviceInfo.Text = device.FormatInfo;
                    MicrophoneDeviceInfo.Visibility = Visibility.Visible;
                    
                    // Update diagnostics
                    AudioDiagnosticsText.Text = device.DiagnosticsInfo;
                    return;
                }
            }
        }
        MicrophoneDeviceInfo.Visibility = Visibility.Collapsed;
        AudioDiagnosticsText.Text = "Select a microphone to see diagnostics";
    }

    private void PreBufferCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        var isEnabled = PreBufferCheckbox.IsChecked == true;
        App.Settings.General.PreRecordingBuffer = isEnabled;
        App.Settings.Save();
        
        // Start or stop the pre-buffer based on setting
        if (isEnabled)
        {
            App.Audio.StartPreBuffer();
        }
        else
        {
            App.Audio.StopPreBuffer();
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
        }
        else
        {
            // Keep default in textbox
        }

        // Llama Model Download Path
        var llamaDownloadPath = App.Settings.General.LlamaDownloadPath;
        if (!string.IsNullOrEmpty(llamaDownloadPath))
        {
            LlamaDownloadPathTextBox.Text = llamaDownloadPath;
        }
        else
        {
            // Keep default in textbox
        }

        // Download Paths Expander state
        DownloadPathsExpander.IsExpanded = App.Settings.General.DownloadPathsExpanded;

        // Pre-recording buffer
        PreBufferCheckbox.IsChecked = App.Settings.General.PreRecordingBuffer;

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
        
        // Update Quick Start hotkey display and instructions based on recording mode
        UpdateQuickStartInstructions();
        
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

    public async void UpdateLastTranscription(string text, double audioDuration = 0, double processingTime = 0)
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] UpdateLastTranscription called with: '{text}'");
        Dispatcher.Invoke(() =>
        {
            // Don't switch tabs - stay on current tab
            
            if (string.IsNullOrEmpty(text))
            {
                LastTranscriptionText.Text = "No transcriptions yet";
                LastTranscriptionStats.Text = "";
            }
            else
            {
                LastTranscriptionText.Text = text;
            }
            System.Diagnostics.Debug.WriteLine($"[DEBUG] LastTranscriptionText.Text is now: '{LastTranscriptionText.Text}'");
        });
        
        // Try to get the latest transcription's duration from database
        try
        {
            var records = await App.Database.GetTranscriptionsAsync(1);
            if (records.Any())
            {
                var latest = records.First();
                var wordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                
                var statsText = $"{wordCount} word{(wordCount != 1 ? "s" : "")}";
                if (latest.Duration > 0)
                {
                    statsText += $" · {latest.Duration:F1}s audio";
                }
                
                Dispatcher.Invoke(() =>
                {
                    LastTranscriptionStats.Text = statsText;
                });
            }
        }
        catch
        {
            // Ignore errors getting stats
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            // Get records from database
            var records = await App.Database.GetTranscriptionsAsync();
            LoggingService.Debug($"[HISTORY] Loaded {records.Count} records from database");

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

            LoggingService.Debug($"[HISTORY] Created {items.Count} list items");

            // Apply grouping
            var groupedItems = ApplyGrouping(items);

            LoggingService.Debug($"[HISTORY] Grouped items count: {groupedItems.Count}");

            // Update on UI thread using BeginInvoke for async execution
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                LoggingService.Debug("[HISTORY] Updating ListBox ItemsSource");
                HistoryListBox.ItemsSource = null;  // Clear first
                HistoryListBox.ItemsSource = groupedItems;
                LoggingService.Debug($"[HISTORY] ListBox now has {HistoryListBox.Items.Count} items");
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Show/hide empty state based on item count
            UpdateEmptyState(items.Count);

            // Update today's session stats
            _ = UpdateSessionStatsAsync();
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to load history");
            System.Windows.MessageBox.Show($"Error loading history: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async Task UpdateSessionStatsAsync()
    {
        try
        {
            // Get today's stats
            var todayRecords = await App.Database.GetTodayTranscriptionsAsync();
            
            var todayCount = todayRecords.Count;
            var todayWords = todayRecords.Sum(r => 
                r.Text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length);
            
            // Calculate today's average audio duration and processing time
            var avgDuration = todayCount > 0 ? todayRecords.Where(r => r.Duration.HasValue).Average(r => r.Duration ?? 0) : 0;
            var avgProcessingTime = todayCount > 0 ? todayRecords.Where(r => r.ProcessingTime.HasValue).Average(r => r.ProcessingTime ?? 0) : 0;
            
            // Calculate today's words per minute
            var totalDuration = todayRecords.Where(r => r.Duration.HasValue).Sum(r => r.Duration ?? 0);
            var wpm = totalDuration > 0 ? todayWords / (totalDuration / 60.0) : 0;
            
            // Get total stats
            var (totalCount, totalWords, totalDurationAll) = await App.Database.GetTotalStatsAsync();
            
            // Build today's stats text
            var todayText = $"Today: {todayCount} transcriptions · {todayWords:N0} words";
            if (todayCount > 0)
            {
                if (avgDuration > 0)
                    todayText += $" · Avg. {avgDuration:F1}s";
                if (avgProcessingTime > 0)
                    todayText += $" · {avgProcessingTime:F1}s";
                if (wpm > 0)
                    todayText += $" · {wpm:F0} wpm";
            }
            
            // Build total stats text
            var totalText = $"Total: {totalCount:N0} transcriptions · {totalWords:N0} words";
            if (totalCount > 0 && totalDurationAll > 0)
            {
                var totalWpm = totalWords / (totalDurationAll / 60.0);
                if (totalWpm > 0)
                    totalText += $" · {totalWpm:F0} wpm";
            }
            
            Dispatcher.Invoke(() =>
            {
                if (SessionStatsText != null)
                {
                    SessionStatsText.Text = todayText;
                }
                if (TotalStatsText != null)
                {
                    TotalStatsText.Text = totalText;
                }
            });
        }
        catch
        {
            // Ignore errors getting stats
        }
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
    
    private void SessionStatsButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to History tab and filter to today
        MainTabControl.SelectedIndex = 2;
        // TODO: Implement date filtering in History tab - for now just navigate to History
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

    private void UpdateQuickStartInstructions()
    {
        try
        {
            var hotkey = App.Settings?.General?.HotkeyTriggerKey?.ToUpper() ?? "R";
            var isPushToTalk = App.Settings?.General?.PushToTalkMode ?? true;
            
            if (QuickStartInstruction1 == null || QuickStartInstruction3 == null)
                return;
            
            if (isPushToTalk)
            {
                // Push to Talk mode: hold to record, release to transcribe
                QuickStartInstruction1.Text = $"1. Hold Ctrl+Shift+{hotkey} to start recording";
                QuickStartInstruction3.Text = "3. Release to transcribe - text automatically appears in your target app";
            }
            else
            {
                // Toggle mode: press to start, press again to stop
                QuickStartInstruction1.Text = $"1. Press Ctrl+Shift+{hotkey} to start/stop recording";
                QuickStartInstruction3.Text = "3. Press again to stop and transcribe - text automatically appears in your target app";
            }
        }
        catch
        {
            // Ignore errors updating instructions
        }
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
        var processingStopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Create cancellation token for this transcription
        _transcriptionCts?.Cancel();  // Cancel any previous transcription
        _transcriptionCts = new CancellationTokenSource();
        var cancellationToken = _transcriptionCts.Token;
        
        try
        {
            LoggingService.Debug("[DEBUG] Starting Whisper transcription...");
            var text = await App.Whisper.TranscribeAsync(audioFile, cancellationToken) ?? string.Empty;
            //LoggingService.Debug($"[DEBUG] Whisper transcription complete: {text}");
            LoggingService.Info("[Whisper] Whisper transcription complete: " + text);
            
            processingStopwatch.Stop();
            var processingTime = processingStopwatch.Elapsed.TotalSeconds;

            LoggingService.Debug($"[DEBUG] llama Enabled: {App.Settings.Llama.Enabled}, Llama Loaded: {App.Llama.IsLoaded}");

            if (App.Settings.Llama.Enabled && App.Llama.IsLoaded && !string.IsNullOrEmpty(text))
            {
                // Check if translation is enabled
                if (App.Settings.Llama.Translate)
                {
                    LoggingService.Debug("[DEBUG] Running Llama translation...");
                    var targetLang = App.Settings.Llama.TranslateTo ?? "en";
                    text = await App.Llama.TranslateTextAsync(text, targetLang, _transcriptionCts?.Token ?? default);
                    LoggingService.Debug($"[LLAMA] Llama translation complete to {targetLang}: {text}");
                }
                else
                {
                    LoggingService.Debug("[DEBUG] Running Llama text formatting...");
                    text = await App.Llama.FormatTextAsync(text, _transcriptionCts?.Token ?? default);
                    LoggingService.Debug($"[LLAMA] Llama formatting complete: {text}");
                }
            }

            // Clipboard operations must run on the UI thread (STA mode)
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // CRITICAL: Use the clipboard that was captured IMMEDIATELY when hotkey was pressed
                    // This prevents race conditions where other apps (like Teams) modify clipboard
                    // when our window gains focus
                    var previousClipboard = App.Hotkey.GetPreviousClipboard();
                    var previousWindow = App.Hotkey.GetPreviousWindow();
                    
                    if (string.IsNullOrEmpty(text))
                    {
                        return;
                    }
                    
                    // Set the transcribed text to clipboard
                    App.Clipboard.SetText(text);

                    // Paste to previous window (now uses retry mechanism internally)
                    await App.Clipboard.PasteToWindow(previousWindow);
                }
                catch (Exception ex)
                {
                    LoggingService.Error(ex, "[CLIPBOARD] Error");
                }
            });

            await App.Database.AddTranscriptionAsync(text ?? string.Empty, App.Audio.LastRecordingDuration, processingTime,
                App.Settings.Whisper.ModelPath, App.Settings.Whisper.Language);

            // UI updates must run on the UI thread
            await Dispatcher.InvokeAsync(async () =>
            {
                var duration = App.Audio.LastRecordingDuration;
                UpdateLastTranscription(text ?? string.Empty, duration, 0);
                await LoadHistoryAsync();
            });

            // Cleanup
            if (System.IO.File.Exists(audioFile))
            {
                System.IO.File.Delete(audioFile);
            }
        }
        catch (OperationCanceledException)
        {
            LoggingService.Info("Transcription cancelled by user");
            System.Windows.MessageBox.Show("Transcription was cancelled.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Transcription error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            App.IsTranscribing = false;
            _transcriptionCts?.Dispose();
            _transcriptionCts = null;
        }
    }

    private void MicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicrophoneComboBox.SelectedItem is ComboBoxItem item)
        {
            App.Settings.Audio.DeviceId = item.Tag?.ToString() ?? "";
            App.Settings.Save();
            UpdateMicrophoneDeviceInfo();
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
        RefreshWordChips();
    }
    
    private void RefreshWordChips()
    {
        var filter = WordFilterTextBox?.Text ?? "";
        WordChipsPanel.Children.Clear();
        
        var words = CustomVocabularyTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => !string.IsNullOrEmpty(w))
            .ToList();
        
        // Apply filter
        if (!string.IsNullOrWhiteSpace(filter))
        {
            words = words.Where(w => w.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        
        WordCountText.Text = $"{words.Count} word{(words.Count != 1 ? "s" : "")}";
        
        var duplicates = words.GroupBy(w => w.ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        foreach (var word in words)
        {
            var chip = CreateWordChip(word, duplicates.Contains(word.ToLowerInvariant()));
            WordChipsPanel.Children.Add(chip);
        }
        
        // Also refresh suggested words state
        RefreshSuggestedWords();
    }
    
    private void WordFilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshWordChips();
    }
    
    private void ReplacementFilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var filter = ReplacementFilterTextBox?.Text ?? "";
        
        if (string.IsNullOrWhiteSpace(filter))
        {
            ReplacementsListBox.ItemsSource = App.Settings.Whisper.WordReplacements;
        }
        else
        {
            var filtered = App.Settings.Whisper.WordReplacements
                .Where(r => r.Source.Contains(filter, StringComparison.OrdinalIgnoreCase) || 
                           r.Replacement.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ReplacementsListBox.ItemsSource = filtered;
        }
    }
    
    private void AddSuggestedWord_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string newWord)
        {
            var words = CustomVocabularyTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim())
                .Where(w => !string.IsNullOrEmpty(w))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            // Add word if not already present
            if (!words.Contains(newWord))
            {
                var currentText = CustomVocabularyTextBox.Text;
                var newText = string.IsNullOrWhiteSpace(currentText) 
                    ? newWord 
                    : currentText.TrimEnd() + Environment.NewLine + newWord;
                CustomVocabularyTextBox.Text = newText;
                App.Settings.Whisper.CustomVocabulary = newText;
                App.Settings.Save();
                RefreshWordChips();
            }
            
            // Refresh suggested words to show checkmarks
            RefreshSuggestedWords();
        }
    }
    
    private void RefreshSuggestedWords()
    {
        var words = CustomVocabularyTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => !string.IsNullOrEmpty(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // Update each suggested word button
        UpdateSuggestedWordButton(SuggestedDocker, "Docker", words);
        UpdateSuggestedWordButton(SuggestedKubectl, "kubectl", words);
        UpdateSuggestedWordButton(SuggestedJSON, "JSON", words);
        UpdateSuggestedWordButton(SuggestedAsync, "async", words);
        UpdateSuggestedWordButton(SuggestedEnum, "enum", words);
        UpdateSuggestedWordButton(SuggestedAPI, "API", words);
        UpdateSuggestedWordButton(SuggestedSDK, "SDK", words);
        UpdateSuggestedWordButton(SuggestedAWS, "AWS", words);
        UpdateSuggestedWordButton(SuggestedKubernetes, "Kubernetes", words);
        UpdateSuggestedWordButton(SuggestedCICD, "CI/CD", words);
        UpdateSuggestedWordButton(SuggestedMiddleware, "middleware", words);
        UpdateSuggestedWordButton(SuggestedBool, "bool", words);
        UpdateSuggestedWordButton(SuggestedNullable, "nullable", words);
        UpdateSuggestedWordButton(SuggestedWebSocket, "WebSocket", words);
        UpdateSuggestedWordButton(SuggestedLocalhost, "localhost", words);
        UpdateSuggestedWordButton(SuggestedGit, "git", words);
    }
    
    private void UpdateSuggestedWordButton(System.Windows.Controls.Button button, string word, HashSet<string> existingWords)
    {
        if (button == null) return;
        
        if (existingWords.Contains(word))
        {
            button.Content = $"✓ {word}";
            button.IsEnabled = false;
            button.Opacity = 0.6;
        }
        else
        {
            button.Content = word;
            button.IsEnabled = true;
            button.Opacity = 1.0;
        }
    }
    
    private System.Windows.Controls.Border CreateWordChip(string word, bool isDuplicate)
    {
        var chip = new System.Windows.Controls.Border
        {
            Background = isDuplicate 
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36)) // Yellow for duplicate
                : (System.Windows.Media.Brush)FindResource("AccentBrush"),
            CornerRadius = new System.Windows.CornerRadius(12),
            Padding = new System.Windows.Thickness(8, 4, 4, 4),
            Margin = new System.Windows.Thickness(0, 0, 6, 6)
        };
        
        var stack = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };
        
        var text = new System.Windows.Controls.TextBlock
        {
            Text = word,
            Foreground = System.Windows.Media.Brushes.White,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(0, 0, 8, 0)
        };
        stack.Children.Add(text);
        
        var closeBtn = new System.Windows.Controls.Button
        {
            Content = "×",
            Tag = word,
            Padding = new System.Windows.Thickness(4, 0, 4, 0),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new System.Windows.Thickness(0),
            Foreground = System.Windows.Media.Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 14,
            FontWeight = System.Windows.FontWeights.Bold
        };
        closeBtn.Click += RemoveWordChip_Click;
        stack.Children.Add(closeBtn);
        
        chip.Child = stack;
        return chip;
    }
    
    private void RemoveWordChip_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string wordToRemove)
        {
            var words = CustomVocabularyTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim())
                .Where(w => !string.IsNullOrEmpty(w) && w != wordToRemove)
                .ToList();
            
            CustomVocabularyTextBox.Text = string.Join(Environment.NewLine, words);
            App.Settings.Save();
            RefreshWordChips();
            RefreshSuggestedWords();
        }
    }
    
    private void AddWord_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        AddWordToVocabulary();
    }
    
    private void AddWordTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            AddWordToVocabulary();
            e.Handled = true;
        }
    }
    
    private void AddWordToVocabulary()
    {
        var newWord = AddWordTextBox.Text.Trim();
        if (string.IsNullOrEmpty(newWord)) return;
        
        var words = CustomVocabularyTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => !string.IsNullOrEmpty(w))
            .ToList();
        
        if (!words.Contains(newWord, StringComparer.OrdinalIgnoreCase))
        {
            words.Add(newWord);
            CustomVocabularyTextBox.Text = string.Join(Environment.NewLine, words);
            RefreshWordChips();
        }
        
        AddWordTextBox.Text = "";
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
    
    private void TestReplacements_Click(object sender, RoutedEventArgs e)
    {
        ApplyReplacementsAndShowOutput();
    }
    
    private void TestReplacementInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyReplacementsAndShowOutput();
    }
    
    private void ApplyReplacementsAndShowOutput()
    {
        var input = TestReplacementInput?.Text ?? "";
        
        if (string.IsNullOrEmpty(input))
        {
            TestReplacementOutput.Text = "";
            return;
        }
        
        var result = input;
        
        foreach (var replacement in App.Settings.Whisper.WordReplacements)
        {
            try
            {
                if (replacement.IsRegex)
                {
                    var options = replacement.CaseSensitive 
                        ? System.Text.RegularExpressions.RegexOptions.None 
                        : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                    result = System.Text.RegularExpressions.Regex.Replace(result, replacement.Source, replacement.Replacement, options);
                }
                else
                {
                    var comparison = replacement.CaseSensitive 
                        ? StringComparison.Ordinal 
                        : StringComparison.OrdinalIgnoreCase;
                    result = result.Replace(replacement.Source, replacement.Replacement, comparison);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"[Replacements] Error applying replacement '{replacement.Source}': {ex.Message}");
            }
        }
        
        // Show result in output textbox
        TestReplacementOutput.Text = result;
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
            WhisperStatusText.Text = "Download path changed - restart to apply";
        }
    }

    private void BrowseLlamaDownloadPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder for GGUF model downloads (must be within AppData, LocalAppData, or UserProfile)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        // Set initial directory to saved path if allowed, otherwise use default
        var savedPath = App.Settings.General.LlamaDownloadPath;
        if (!string.IsNullOrEmpty(savedPath) && IsPathAllowed(savedPath))
        {
            dialog.SelectedPath = savedPath;
        }
        else if (!string.IsNullOrEmpty(App.LlamaModelsPath))
        {
            dialog.SelectedPath = App.LlamaModelsPath;
        }
        else
        {
            // Default to user's Documents folder (which is within UserProfile)
            dialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var selectedPath = dialog.SelectedPath;
            
            // Validate the selected path is within allowed directories
            if (!IsPathAllowed(selectedPath))
            {
                System.Windows.MessageBox.Show(
                    $"The selected path is not allowed:\n\n{selectedPath}\n\n" +
                    "Please choose a folder within:\n" +
                    "• AppData (e.g., %APPDATA%)\\whisperMeOff\\Models\\...\n" +
                    "• LocalAppData (e.g., %LOCALAPPDATA%)\\whisperMeOff\\Models\\...\n" +
                    "• Your UserProfile (e.g., C:\\Users\\YourName\\...)\\Models\\...",
                    "Path Not Allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            LlamaDownloadPathTextBox.Text = selectedPath;
            App.Settings.General.LlamaDownloadPath = selectedPath;
            App.Settings.Save();
            LlamaStatusText.Text = "Download path changed - restart to apply";
        }
    }

    /// <summary>
    /// Checks if a path is within allowed directories for downloads
    /// </summary>
    private bool IsPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var fullPath = System.IO.Path.GetFullPath(path);

            // Check if path starts with any allowed base path
            string[] allowedBases = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            foreach (var basePath in allowedBases)
            {
                var normalizedBase = System.IO.Path.GetFullPath(basePath).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                var normalizedPath = fullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;

                if (normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Also allow paths within the app's models directory
            if (!string.IsNullOrEmpty(App.ModelsPath))
            {
                var appBase = System.IO.Path.GetFullPath(App.ModelsPath).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                var normalizedAppPath = fullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;

                if (normalizedAppPath.StartsWith(appBase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void DownloadPathsExpander_Expanded(object sender, RoutedEventArgs e)
    {
        App.Settings.General.DownloadPathsExpanded = true;
        App.Settings.Save();
    }

    private void DownloadPathsExpander_Collapsed(object sender, RoutedEventArgs e)
    {
        App.Settings.General.DownloadPathsExpanded = false;
        App.Settings.Save();
    }

    private void LlamaDownloadPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // When path changes, update status text
        var newPath = LlamaDownloadPathTextBox.Text;
        if (!string.IsNullOrEmpty(newPath) && System.IO.Directory.Exists(newPath))
        {
            LlamaPathValidIcon.Visibility = Visibility.Visible;
            
            // Count GGUF model files and calculate size
            var files = System.IO.Directory.GetFiles(newPath, "*.gguf");
            var modelCount = files.Length;
            long totalSize = 0;
            foreach (var file in files)
            {
                try { totalSize += new System.IO.FileInfo(file).Length; }
                catch (Exception ex) { LoggingService.Debug($"[UI] Error getting file size: {ex.Message}"); }
            }
            
            // Format size
            string sizeText;
            if (totalSize >= 1024L * 1024 * 1024)
                sizeText = $"{totalSize / (1024.0 * 1024 * 1024):F1} GB";
            else if (totalSize >= 1024 * 1024)
                sizeText = $"{totalSize / (1024.0 * 1024):F0} MB";
            else
                sizeText = $"{totalSize / 1024.0:F0} KB";
            
            if (modelCount > 0)
            {
                LlamaModelCountText.Text = $"{modelCount} model{(modelCount != 1 ? "s" : "")} · {sizeText}";
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
            WhisperPathValidIcon.Visibility = Visibility.Visible;
            WhisperPathWarningIcon.Visibility = Visibility.Collapsed;
            
            // Count model files and calculate size
            var files = System.IO.Directory.GetFiles(newPath, "*.*")
                .Where(f => f.EndsWith(".bin") || f.EndsWith(".gguf") || f.EndsWith(".txt") || f.EndsWith(".json"))
                .ToArray();
            
            var modelCount = files.Length;
            long totalSize = 0;
            foreach (var file in files)
            {
                try { totalSize += new System.IO.FileInfo(file).Length; }
                catch (Exception ex) { LoggingService.Debug($"[UI] Error getting file size: {ex.Message}"); }
            }
            
            // Format size
            string sizeText;
            if (totalSize >= 1024L * 1024 * 1024)
                sizeText = $"{totalSize / (1024.0 * 1024 * 1024):F1} GB";
            else if (totalSize >= 1024 * 1024)
                sizeText = $"{totalSize / (1024.0 * 1024):F0} MB";
            else
                sizeText = $"{totalSize / 1024.0:F0} KB";
            
            if (modelCount > 0)
            {
                WhisperModelCountText.Text = $"{modelCount} model{(modelCount != 1 ? "s" : "")} · {sizeText}";
                WhisperModelCountBadge.Visibility = Visibility.Visible;
            }
            else
            {
                WhisperModelCountBadge.Visibility = Visibility.Collapsed;
            }
            
            // Check disk space
            try
            {
                var pathRoot = System.IO.Path.GetPathRoot(newPath);
                if (string.IsNullOrEmpty(pathRoot)) return;
                var drive = new System.IO.DriveInfo(pathRoot);
                if (drive.IsReady)
                {
                    var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    if (freeSpaceGB < 10)
                    {
                        WhisperPathWarningIcon.Visibility = Visibility.Visible;
                        WhisperPathWarningIcon.ToolTip = $"Only {freeSpaceGB:F1} GB free on {drive.Name} - may need more space for models";
                        WhisperPathValidIcon.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex) { LoggingService.Debug($"[UI] Error checking disk space: {ex.Message}"); }
        }
        else
        {
            WhisperPathValidIcon.Visibility = Visibility.Collapsed;
            WhisperPathWarningIcon.Visibility = Visibility.Collapsed;
            WhisperModelCountBadge.Visibility = Visibility.Collapsed;
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

    private CancellationTokenSource? _llamaDownloadCts;
    private bool _isDownloading = false;

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
        LlamaDownloadControlsPanel.Visibility = Visibility.Visible;
        LlamaDownloadStatusText.Text = "Searching for model...";
        LlamaDownloadProgressBar.Value = 0;
        CancelLlamaDownloadButton.IsEnabled = true;
        _isDownloading = true;

        // Create cancellation token for this download
        _llamaDownloadCts = new CancellationTokenSource();

        var progress = new Progress<double>(p =>
        {
            LlamaDownloadProgressBar.Value = p;
            LlamaDownloadStatusText.Text = $"Downloading... {p:F0}%";
        });

        var path = await App.ModelDownload.DownloadLlamaModelAsync(modelId, progress, _llamaDownloadCts.Token);

        _isDownloading = false;
        LlamaDownloadProgressBar.Visibility = Visibility.Collapsed;
        LlamaDownloadControlsPanel.Visibility = Visibility.Collapsed;
        _llamaDownloadCts?.Dispose();
        _llamaDownloadCts = null;

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
        else if (string.IsNullOrEmpty(path))
        {
            LlamaDownloadStatusText.Text = "Could not find a GGUF file for this model";
            System.Windows.MessageBox.Show(
                "Could not find a GGUF file for this model.\n\n" +
                "Note: Not all HuggingFace models have GGUF files. Look for:\n" +
                "• Models with 'GGUF' in the name (e.g., 'bartowski/gemma-2b-it-GGUF')\n" +
                "• Models from 'TheBloke' or 'bartowski' repos\n" +
                "• Quantized models (look for Q4_K_M, Q5_K_S, etc. in the name)",
                "GGUF Model Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (path.StartsWith("ERROR:Download path is not allowed"))
        {
            LlamaDownloadStatusText.Text = "Download path not allowed";
            System.Windows.MessageBox.Show(
                path.Substring(6) + "\n\n" +
                "To fix this:\n" +
                "1. Go to Settings > General\n" +
                "2. Change 'Llama Download Path' to a folder within:\n" +
                "   • AppData (e.g., %APPDATA%\\whisperMeOff\\Models)\n" +
                "   • LocalAppData (e.g., %LOCALAPPDATA%\\whisperMeOff\\Models)\n" +
                "   • Your UserProfile (e.g., C:\\Users\\YourName\\Models)",
                "Download Path Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            LlamaDownloadStatusText.Text = "Download failed";
            System.Windows.MessageBox.Show(
                "Download failed: " + path + "\n\n" +
                "Make sure the model ID is correct and the model has GGUF files available.",
                "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await Task.Delay(2000);
        LlamaDownloadProgressBar.Visibility = Visibility.Collapsed;
        LlamaDownloadControlsPanel.Visibility = Visibility.Collapsed;
    }

    private void CancelLlamaDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_llamaDownloadCts != null && _isDownloading)
        {
            LoggingService.Info("[UI] Cancelling Llama download...");
            _llamaDownloadCts.Cancel();
            CancelLlamaDownloadButton.IsEnabled = false;
            LlamaDownloadStatusText.Text = "Cancelling...";
        }
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
            HuggingFaceTokenTextBox.Text = HuggingFaceTokenBox.Password ?? string.Empty;
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
            HuggingFaceTokenTextBox.Text = HuggingFaceTokenBox.Password ?? string.Empty;
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
            // Light themes (A-Z), then Dark themes (A-Z)
            string[] themes = { 
                "AyuLight", "CatppuccinLatte", "Daylight", "EverforestLight", "GitHubLight", "Light", "OneLight", "SolarizedLight",
                "Dark", "Dracula", "Gruvbox", "Monokai", "NightOwl", "Nord", "Synthwave", "TokyoNight" 
            };
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
        
        // Update Quick Start instructions to reflect the new mode
        UpdateQuickStartInstructions();
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
    
    private void CopyLastTranscription_Click(object sender, RoutedEventArgs e)
    {
        var text = LastTranscriptionText?.Text ?? "";
        if (!string.IsNullOrEmpty(text) && text != "No transcriptions yet")
        {
            App.Clipboard.SetText(text);
        }
    }
    
    // Dashboard click handlers - navigate to relevant tabs
    private void WhisperStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Switch to Whisper tab (index 3)
        MainTabControl.SelectedIndex = 3;
    }
    
    private void LlamaStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Switch to Llama tab (index 4)
        MainTabControl.SelectedIndex = 4;
    }
    
    private void MicrophoneStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Switch to Audio tab (index 2)
        MainTabControl.SelectedIndex = 2;
    }
    
    private void PresetStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Switch to General tab (index 1)
        MainTabControl.SelectedIndex = 1;
    }
    
    private void HotkeyStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Switch to General tab (index 1)
        MainTabControl.SelectedIndex = 1;
    }
    
    private async void LoadLlama_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Load Llama model
        if (!string.IsNullOrEmpty(App.Settings.Llama.ModelPath))
        {
            await App.Llama.InitializeAsync(App.Settings.Llama.ModelPath);
            // Update status
            LlamaStatusText.Text = "Ready";
            LlamaLoadLink.Visibility = Visibility.Collapsed;
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

    private TransformationDirection _currentDirection = TransformationDirection.Formal;
    private TransformationType _currentType = TransformationType.Tone;

    private void StyleBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border badge && badge.Tag is string tag)
        {
            // Reset all badges to unselected state
            FormalBadge.Background = FindResource("CardBrush") as System.Windows.Media.Brush;
            FormalBadge.BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush;
            ((TextBlock)FormalBadge.Child).Foreground = FindResource("TextBrush") as System.Windows.Media.Brush;
            ((TextBlock)FormalBadge.Child).FontWeight = System.Windows.FontWeights.Normal;
            
            InformalBadge.Background = FindResource("CardBrush") as System.Windows.Media.Brush;
            InformalBadge.BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush;
            ((TextBlock)InformalBadge.Child).Foreground = FindResource("TextBrush") as System.Windows.Media.Brush;
            ((TextBlock)InformalBadge.Child).FontWeight = System.Windows.FontWeights.Normal;
            
            CreativeBadge.Background = FindResource("CardBrush") as System.Windows.Media.Brush;
            CreativeBadge.BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush;
            ((TextBlock)CreativeBadge.Child).Foreground = FindResource("TextBrush") as System.Windows.Media.Brush;
            ((TextBlock)CreativeBadge.Child).FontWeight = System.Windows.FontWeights.Normal;
            
            HumorBadge.Background = FindResource("CardBrush") as System.Windows.Media.Brush;
            HumorBadge.BorderBrush = FindResource("BorderBrush") as System.Windows.Media.Brush;
            ((TextBlock)HumorBadge.Child).Foreground = FindResource("TextBrush") as System.Windows.Media.Brush;
            ((TextBlock)HumorBadge.Child).FontWeight = System.Windows.FontWeights.Normal;
            
            // Highlight selected badge
            badge.Background = FindResource("AccentBrush") as System.Windows.Media.Brush;
            badge.BorderBrush = FindResource("AccentBrush") as System.Windows.Media.Brush;
            badge.BorderThickness = new System.Windows.Thickness(1);
            ((TextBlock)badge.Child).Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            ((TextBlock)badge.Child).FontWeight = System.Windows.FontWeights.SemiBold;
            
            // Map tag selection to type and direction
            switch (tag)
            {
                case "Formal":
                    _currentType = TransformationType.Tone;
                    _currentDirection = TransformationDirection.Formal;
                    break;
                case "Informal":
                    _currentType = TransformationType.Tone;
                    _currentDirection = TransformationDirection.Informal;
                    break;
                case "Creative":
                    _currentType = TransformationType.Creative;
                    _currentDirection = TransformationDirection.Default;
                    break;
                case "Humor":
                    _currentType = TransformationType.Humor;
                    _currentDirection = TransformationDirection.Default;
                    break;
            }
        }
    }

    private void FormalPromptTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Save custom formal prompt when user finishes editing
        if (FormalPromptTextBox != null && App.Settings?.Transformation != null)
        {
            App.Settings.Transformation.CustomFormalPrompt = FormalPromptTextBox.Text;
            App.Settings.Save();
            LoggingService.Debug("[Transform] Custom formal prompt saved");
        }
    }

    private void InformalPromptTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Save custom informal prompt when user finishes editing
        if (InformalPromptTextBox != null && App.Settings?.Transformation != null)
        {
            App.Settings.Transformation.CustomInformalPrompt = InformalPromptTextBox.Text;
            App.Settings.Save();
            LoggingService.Debug("[Transform] Custom informal prompt saved");
        }
    }

    private void CreativePromptTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Save custom creative prompt when user finishes editing
        if (CreativePromptTextBox != null && App.Settings?.Transformation != null)
        {
            App.Settings.Transformation.CustomCreativePrompt = CreativePromptTextBox.Text;
            App.Settings.Save();
            LoggingService.Debug("[Transform] Custom creative prompt saved");
        }
    }

    private void HumorPromptTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Save custom humor prompt when user finishes editing
        if (HumorPromptTextBox != null && App.Settings?.Transformation != null)
        {
            App.Settings.Transformation.CustomHumorPrompt = HumorPromptTextBox.Text;
            App.Settings.Save();
            LoggingService.Debug("[Transform] Custom humor prompt saved");
        }
    }

    /// <summary>
    /// Saves both custom formal and informal prompts to settings.
    /// Called before transformation to ensure latest prompts are used.
    /// </summary>
    private void SaveCustomPrompts()
    {
        if (App.Settings?.Transformation != null)
        {
            if (FormalPromptTextBox != null)
            {
                App.Settings.Transformation.CustomFormalPrompt = FormalPromptTextBox.Text;
            }
            if (InformalPromptTextBox != null)
            {
                App.Settings.Transformation.CustomInformalPrompt = InformalPromptTextBox.Text;
            }
            if (CreativePromptTextBox != null)
            {
                App.Settings.Transformation.CustomCreativePrompt = CreativePromptTextBox.Text;
            }
            if (HumorPromptTextBox != null)
            {
                App.Settings.Transformation.CustomHumorPrompt = HumorPromptTextBox.Text;
            }
            App.Settings.Save();
            LoggingService.Debug("[Transform] Custom prompts saved before transformation");
        }
    }

    private void SavePromptsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCustomPrompts();
        LoggingService.Debug("[Transform] Custom prompts manually saved by user");
    }

    private void ResetPromptsButton_Click(object sender, RoutedEventArgs e)
    {
        // Reset prompts to defaults
        if (App.Settings?.Transformation != null)
        {
            App.Settings.Transformation.CustomFormalPrompt = "";
            App.Settings.Transformation.CustomInformalPrompt = "";
            App.Settings.Transformation.CustomCreativePrompt = "";
            App.Settings.Transformation.CustomHumorPrompt = "";
            App.Settings.Save();
        }
        
        // Update UI with default prompts
        var defaultFormalPrompt = "Make this text more formal. Keep all the same words and meaning. Only change the tone.";
        var defaultInformalPrompt = "Make this text more informal. Keep all the same words and meaning. Only change the tone.";
        var defaultCreativePrompt = "Transform this text using a creative writing style while preserving the core message.";
        var defaultHumorPrompt = "Transform this text, adjusting the humor and tone level while preserving the core message.";
        
        if (FormalPromptTextBox != null)
            FormalPromptTextBox.Text = defaultFormalPrompt;
        if (InformalPromptTextBox != null)
            InformalPromptTextBox.Text = defaultInformalPrompt;
        if (CreativePromptTextBox != null)
            CreativePromptTextBox.Text = defaultCreativePrompt;
        if (HumorPromptTextBox != null)
            HumorPromptTextBox.Text = defaultHumorPrompt;
        
        LoggingService.Info("[Transform] Prompts reset to defaults");
    }

    private void LoadTransformPromptsToUI()
    {
        // Load custom prompts from settings, or use defaults
        var customFormal = App.Settings?.Transformation?.CustomFormalPrompt ?? "";
        var customInformal = App.Settings?.Transformation?.CustomInformalPrompt ?? "";
        var customCreative = App.Settings?.Transformation?.CustomCreativePrompt ?? "";
        var customHumor = App.Settings?.Transformation?.CustomHumorPrompt ?? "";
        
        if (FormalPromptTextBox != null)
        {
            FormalPromptTextBox.Text = string.IsNullOrWhiteSpace(customFormal) 
                ? "Make this text more formal. Keep all the same words and meaning. Only change the tone."
                : customFormal;
        }
        
        if (InformalPromptTextBox != null)
        {
            InformalPromptTextBox.Text = string.IsNullOrWhiteSpace(customInformal) 
                ? "Make this text more informal. Keep all the same words and meaning. Only change the tone."
                : customInformal;
        }
        
        if (CreativePromptTextBox != null)
        {
            CreativePromptTextBox.Text = string.IsNullOrWhiteSpace(customCreative) 
                ? "Transform this text using a creative writing style while preserving the core message."
                : customCreative;
        }
        
        if (HumorPromptTextBox != null)
        {
            HumorPromptTextBox.Text = string.IsNullOrWhiteSpace(customHumor) 
                ? "Transform this text, adjusting the humor and tone level while preserving the core message."
                : customHumor;
        }
    }

    private async void TransformButton_Click(object sender, RoutedEventArgs e)
    {
        LoggingService.Debug("[DEBUG] TransformButton_Click called");
        
        // Ensure custom prompts are saved before transformation
        SaveCustomPrompts();
        
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
                TransformationType = _currentType,
                Direction = _currentDirection,
                PreserveProperNouns = true,
                PreserveTechnicalTerms = true,
                MinQualityThreshold = 70,
                IncludeQualityMetrics = true
            };
            
            var result = await App.Transform.TransformAsync(request);
            LoggingService.Debug($"[DEBUG] TransformAsync completed. Success: {result.Success}, Error: {result.ErrorMessage}, TransformedText length: {result.TransformedText?.Length ?? -1}");
            
            if (result.Success)
            {
                LoggingService.Debug($"[DEBUG] Setting output text box with: {(result.TransformedText?.Length > 100 ? result.TransformedText.Substring(0, 100) + "..." : result.TransformedText)}");
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
        if (!string.IsNullOrEmpty(TransformOutputTextBox.Text))
        {
            System.Windows.Clipboard.SetText(TransformOutputTextBox.Text);
            
            // Clear both fields to indicate transformation was applied
            TransformInputTextBox.Text = string.Empty;
            TransformOutputTextBox.Text = string.Empty;
            
            TransformStatusText.Text = "Transformed text applied (copied to clipboard, fields cleared).";
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
    
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
