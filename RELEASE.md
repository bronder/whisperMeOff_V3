# Release Notes

## Latest Release

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

## Support

For issues and feature requests, please open an issue on the project repository.
