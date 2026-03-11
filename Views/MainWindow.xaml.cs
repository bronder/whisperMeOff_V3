using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using whisperMeOff.Services;

namespace whisperMeOff.Views;

public partial class MainWindow : Window
{
    private bool _isProcessing = false;
    
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Initialize hotkey
        App.Hotkey.Initialize(this);

        // Load audio devices
        LoadAudioDevices();
     
        // Load settings to UI
        LoadSettingsToUI();

        // Subscribe to Llama model load/unload events
        App.Llama.ModelLoaded += (s, isLoaded) => Dispatcher.Invoke(() =>
        {
            if (App.Settings.Llama.Enabled)
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
            DownloadPathStatusText.Text = $"Using: {downloadPath}";
        }
        else
        {
            DownloadPathStatusText.Text = $"Default: {App.WhisperModelsPath}";
        }

        // Llama Model Download Path
        var llamaDownloadPath = App.Settings.General.LlamaDownloadPath;
        if (!string.IsNullOrEmpty(llamaDownloadPath))
        {
            LlamaDownloadPathTextBox.Text = llamaDownloadPath;
            LlamaDownloadPathStatusText.Text = $"Using: {llamaDownloadPath}";
        }
        else
        {
            LlamaDownloadPathStatusText.Text = $"Default: {App.LlamaModelsPath}";
        }

        // Model path
        var modelPath = App.Whisper.GetModelPath();
        if (!string.IsNullOrEmpty(modelPath))
        {
            ModelPathText.Text = System.IO.Path.GetFileName(modelPath);
            WhisperStatusText.Text = "Ready";
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
        }

        // HuggingFace Model ID
        var modelId = App.Settings.Llama.ModelId;
        if (!string.IsNullOrEmpty(modelId))
        {
            HuggingFaceModelIdTextBox.Text = modelId;
        }

        // HuggingFace Token (load into PasswordBox)
        var hfToken = App.Settings.Llama.HuggingFaceToken;
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
        System.Diagnostics.Debug.WriteLine("[UI] MainWindow initialized");
    }

    public void NavigateToSettings()
    {
        MainTabControl.SelectedIndex = 1; // Audio tab (or could be Settings tab index)
    }

    public void UpdateLastTranscription(string text)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(text))
            {
                LastTranscriptionText.Text = "No transcriptions yet";
            }
            else
            {
                LastTranscriptionText.Text = text;
            }
        });
    }

    private async Task LoadHistoryAsync()
    {
        var records = await App.Database.GetTranscriptionsAsync();
        var items = records.Select(r => new TranscriptionListItem
        {
            Id = r.Id,
            Text = r.Text,
            DisplayTime = DateTime.Parse(r.Timestamp).ToString("MMM d, yyyy 'at' h:mm tt"),
            Duration = r.Duration
        }).ToList();

        HistoryListBox.ItemsSource = items;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedIndex = 1;
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        // Don't allow starting new recording while processing
        if (_isProcessing)
        {
            System.Diagnostics.Debug.WriteLine("[DEBUG] RecordButton_Click ignored - already processing");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"[DEBUG] RecordButton_Click called. IsRecording={App.Audio.IsRecording}");
        if (App.Audio.IsRecording)
        {
            // Stop recording
            System.Diagnostics.Debug.WriteLine("[DEBUG] Stopping recording via button click");
            _isProcessing = true;
            RecordButton.IsEnabled = false;
            RecordButton.Content = "Processing...";
            Task.Run(async () =>
            {
                var audioFile = await App.Audio.StopRecordingAsync();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] StopRecordingAsync returned: {audioFile}");
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
            System.Diagnostics.Debug.WriteLine("[DEBUG] Starting recording via button click");
            App.Audio.StartRecording();
        }
    }

    private async Task ProcessTranscriptionAsync(string audioFile)
    {
        App.IsTranscribing = true;
        
        try
        {
            System.Diagnostics.Debug.WriteLine("[DEBUG] Starting Whisper transcription...");
            var text = await App.Whisper.TranscribeAsync(audioFile) ?? string.Empty;
            //System.Diagnostics.Debug.WriteLine($"[DEBUG] Whisper transcription complete: {text?.Substring(0, Math.Min(50, text?.Length ?? 0))}...");
            System.Diagnostics.Debug.WriteLine("[Whisper] Whisper transcription complete: " + text);

            System.Diagnostics.Debug.WriteLine("[DEBUG] llama Enabled: " + App.Settings.Llama.Enabled + ", Llama Loaded: " + App.Llama.IsLoaded );

            if (App.Settings.Llama.Enabled && App.Llama.IsLoaded && !string.IsNullOrEmpty(text))
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Running Llama text formatting...");
                text = await App.Llama.FormatTextAsync(text);
                System.Diagnostics.Debug.WriteLine($"[LLAMA] Llama formatting complete: " + text);
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

                    // Paste to previous window
                    await Task.Delay(100);
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
                    System.Diagnostics.Debug.WriteLine($"[CLIPBOARD] Error: {ex.Message}");
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
            DownloadPathStatusText.Text = $"Using: {dialog.SelectedPath}";
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
            LlamaDownloadPathStatusText.Text = $"Using: {dialog.SelectedPath}";
            LlamaStatusText.Text = "Download path changed - restart to apply";
        }
    }

    private void LlamaDownloadPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // When path changes, update status text
        var newPath = LlamaDownloadPathTextBox.Text;
        if (!string.IsNullOrEmpty(newPath) && System.IO.Directory.Exists(newPath))
        {
            LlamaDownloadPathStatusText.Text = $"Using: {newPath}";
        }
        else if (!string.IsNullOrEmpty(newPath))
        {
            LlamaDownloadPathStatusText.Text = $"New path will be created: {newPath}";
        }
        else
        {
            LlamaDownloadPathStatusText.Text = $"Default: {App.LlamaModelsPath}";
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

        // Update status text
        var newPath = ModelDownloadPathTextBox.Text;
        if (!string.IsNullOrEmpty(newPath) && System.IO.Directory.Exists(newPath))
        {
            DownloadPathStatusText.Text = $"Using: {newPath}";
        }
        else if (!string.IsNullOrEmpty(newPath))
        {
            DownloadPathStatusText.Text = $"New path will be created: {newPath}";
        }
        else
        {
            DownloadPathStatusText.Text = $"Default: {App.WhisperModelsPath}";
        }
    }

    private async Task DownloadWhisperModelAsync(string size)
    {
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

            // Update button to show "Re-download" so user can re-download if needed
            var buttonName = $"Download{char.ToUpper(size[0])}{size.Substring(1)}Btn";
            var button = this.FindName(buttonName) as System.Windows.Controls.Button;
            if (button != null)
            {
                button.Content = "Re-download";
            }
        }
        else
        {
            DownloadStatusText.Text = "Download failed";
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
            System.Diagnostics.Debug.WriteLine($"[ERROR] Could not open URL: {ex.Message}");
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

        System.Diagnostics.Debug.WriteLine("Model ID: " + modelId);
        System.Diagnostics.Debug.WriteLine("Download path: " + App.Settings.General.LlamaDownloadPath);
        System.Diagnostics.Debug.WriteLine("LlamaModelsPath: " + App.LlamaModelsPath);

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
        // Don't save during initialization
        if (!_isInitialized) return;
        
        App.Settings.Llama.HuggingFaceToken = HuggingFaceTokenBox.Password;
        App.Settings.Save();
        System.Diagnostics.Debug.WriteLine("[UI] Saved HuggingFaceToken");
    }

    private bool _isInitialized = false;
    
    private void HuggingFaceModelIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Don't save during initialization
        if (!_isInitialized) return;
        
        App.Settings.Llama.ModelId = HuggingFaceModelIdTextBox.Text;
        App.Settings.Save();
        System.Diagnostics.Debug.WriteLine($"[UI] Saved ModelId: {HuggingFaceModelIdTextBox.Text}");
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
            System.Diagnostics.Debug.WriteLine($"[UI] Theme changed to: {theme}");
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
                        System.Diagnostics.Debug.WriteLine("[Startup] Registered for launch at login");
                    }
                }
                else
                {
                    // Remove from startup
                    key.DeleteValue("whisperMeOff", false);
                    System.Diagnostics.Debug.WriteLine("[Startup] Unregistered from launch at login");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Startup] Error: {ex.Message}");
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
                DisplayTime = DateTime.Parse(r.Timestamp).ToString("MMM d, yyyy 'at' h:mm tt"),
                Duration = r.Duration
            }).ToList();

            HistoryListBox.ItemsSource = items;
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
}

public class TranscriptionListItem
{
    public long Id { get; set; }
    public string Text { get; set; } = "";
    public string DisplayTime { get; set; } = "";
    public double? Duration { get; set; }
}
