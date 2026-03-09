# Release Notes

## Latest Release

### Version 1.1.0

#### Features
- **Theme Support**: New visual theme system with 6 themes to choose from:
  - Light (default)
  - Dark
  - Nord (arctic, north-bluish)
  - Dracula
  - Gruvbox
  - Monokai
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

#### Version 1.2.0
- **Audio Visualization**: Real-time waveform display and VU meter during recording
- **Toggle Recording Mode**: Press-to-start/stop recording option (in addition to push-to-talk)
- **Better Hotkey UI**: Improved hotkey configuration interface

#### Version 1.3.0
- **Extended Language Support**: Full language dropdown with 50+ languages
- **History Export**: Export transcription history to JSON/CSV/TXT
- **Keyboard Navigation**: Full keyboard navigation for accessibility

#### Version 2.0.0
- **Custom Vocabulary**: User-defined word lists for better recognition
- **Plugin System**: Extensible formatting pipeline
- **Advanced Settings**: More transcription parameters (temperature, beam size, etc.)

---

## Support

For issues and feature requests, please open an issue on the project repository.
