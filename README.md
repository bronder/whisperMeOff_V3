# whisperMeOff

Offline voice-to-text transcription with auto-paste for Windows.

## Features

- **Voice Recording**: Hold `Ctrl+Shift+R` to start recording, release to transcribe
- **Auto-Paste**: Automatically pastes transcribed text to the previously active window
- **Whisper Transcription**: Uses OpenAI's Whisper model for accurate speech recognition
- **Llama Formatting**: Optional Llama-based text formatting for cleaner output
- **Offline**: Works completely offline - no internet required for transcription
- **HuggingFace Support**: Download GGUF models directly from HuggingFace

## Requirements

- Windows 10/11
- .NET 10.0 Runtime

## Installation

1. Download the latest release
2. Run `whisperMeOff.exe`
3. The app will guide you through initial setup

## Usage

### Quick Start

1. **Select a Whisper Model**: Go to the Whisper tab and download a model
2. **Set Hotkey**: The default is `Ctrl+Shift+R` (you can change this in General settings)
3. **Start Recording**: Hold `Ctrl+Shift+R` to start recording
4. **Release to Transcribe**: Release the keys to stop recording and transcribe
5. **Auto-Paste**: The transcribed text is automatically pasted to your previous window

### Configuration

#### Whisper Settings
- **Language**: Select the language or use "Auto Detect"
- **Translate**: Enable to translate output to English
- **Model**: Select a Whisper model size (tiny, base, small, medium, large)

#### Llama Settings (Optional)
- Enable Llama text formatting for cleaner output
- Download models from HuggingFace (search for GGUF quantized models)
- Enter your HuggingFace token for private models

#### General Settings
- **Hotkey**: Change the trigger key (default: R)
- **Download Paths**: Customize where models are saved

## Model Sizes

| Model | Size | Accuracy |
|-------|------|----------|
| Tiny | ~75 MB | Low |
| Base | ~150 MB | Medium |
| Small | ~500 MB | Good |
| Medium | ~1.5 GB | Better |
| Large | ~3 GB | Best |

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+Shift+R | Start/Stop recording |

## Building from Source

```bash
dotnet build
```

## License

MIT License
