# Release Notes

## Latest Release

### Version 1.2.2

#### Features
- **Launch at Login**: Automatically start whisperMeOff when Windows boots. Enable this option in General settings to have the app launch automatically on system startup.

---

### Version 1.2.0

#### Features
- **Custom Vocabulary**: Define word lists to improve recognition accuracy for domain-specific terminology, names, or frequently used phrases. These words are passed to Whisper as an initial prompt to boost recognition.
- **Word Replacements**: Automatically replace spoken phrases with different text after transcription. Useful for expanding abbreviations, fixing misrecognized words, or creating voice shortcuts.
  - Example: "yaba daba do" → "yum"
  - Example: "API" → "A P I"

#### Improvements
- Added new Vocabulary tab to organize vocabulary and word replacement settings in one place

---

### Version 1.1.1

#### Bug Fixes
- **Llama Text Formatting**: Fixed critical bug where correct text was being discarded during cleanup
- **Output Processing**: Improved cleanup logic to preserve valid model output
- **Llama Version**: Updated to LLamaSharp 0.26.0 with Vulkan GPU support

#### Improvements
- Better prompt engineering for text proofreading tasks
- Greedy sampling (temperature=0) for deterministic output
- More robust anti-prompt handling

---

### Version 1.1.0

#### Features
- **Theme Support**: New visual theme system with 7 themes to choose from:
  - Light (default)
  - Dark
  - Nord (arctic, north-bluish)
  - Dracula
  - Gruvbox
  - Monokai
  - Synthwave (retro-futuristic neon)
- **System Tray Recording**: Right-click the tray icon to start/stop recording without opening the app
- **Minimize to Tray**: Option to minimize to system tray instead of taskbar
- **Theme Persistence**: Selected theme is saved and restored on app restart
- **Llama Status Display**: Improved model status showing whether Llama is loaded

#### Bug Fixes
- **Theme Loading**: Added exception handling to prevent crashes from corrupted theme files

---

### Version 1.0.1

#### Bug Fixes
- **Audio Service**: Fixed disposal tracking to prevent operations after dispose
- **Null Safety**: Improved null handling in transcription pipeline to prevent crashes
- **Hotkey Service**: Fixed keyboard hook initialization for better reliability

#### Improvements
- Removed redundant System.Text.Json dependency (now transitively provided)

---

### Version 1.0.0

#### Features
- Voice recording with push-to-talk hotkey (Ctrl+Shift+R)
- Automatic transcription using Whisper models
- Auto-paste to previous window
- Download Whisper models directly from the app
- Llama text formatting support
- Download GGUF models from HuggingFace
- Custom model download paths
- Audio device selection
- Transcription history

#### New in This Release
- **HuggingFace Integration**: Download GGUF models directly from HuggingFace
- **Improved Clipboard**: Fixed clipboard behavior - text now stays on clipboard long enough to paste
- **Settings Persistence**: HuggingFace ID and model settings now persist between sessions
- **Progress Indicators**: Visual progress for model downloads
- **Better Error Handling**: More informative error messages for failed downloads

## Installation

1. Download `whisperMeOff.exe` from the latest release
2. Run the application
3. Download a Whisper model from the Whisper tab
4. Start using!

## Upgrading

Simply replace the existing `whisperMeOff.exe` with the new version. Your settings will be preserved.

## Supported Whisper Models

- Whisper Tiny (~75 MB)
- Whisper Base (~150 MB)
- Whisper Small (~500 MB)
- Whisper Medium (~1.5 GB)
- Whisper Large (~3 GB)

## Known Issues

- Large models may require more memory
- Some antivirus software may flag the application

---

## Future Releases

### Planned Features

#### Version 1.3.0
- **Audio Visualization**: Real-time waveform display and VU meter during recording
- **Toggle Recording Mode**: Press-to-start/stop recording option (in addition to push-to-talk)
- **History Export**: Export transcription history to JSON/CSV/TXT
- **Extended Language Support**: Full language dropdown with 50+ languages
- **Keyboard Navigation**: Full keyboard navigation for accessibility

#### Version 2.0.0

- **Advanced Settings**: More transcription parameters (temperature, beam size, etc.)

---

## Support

For issues and feature requests, please open an issue on the project repository.
