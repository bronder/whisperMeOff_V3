# whisperMeOff for Windows — Product Requirements Document

**Version:** 1.0
**Date:** 2026-03-08
**Status:** Draft
**Owner:** Product Owner, whisperMeOff
**Target Release:** v1.0 Windows-native

---

## Table of Contents

1. Executive Summary
2. Product Vision
3. User Personas
4. Core User Workflows
5. Feature Requirements
6. Technical Architecture
7. Windows Permission Requirements
8. Performance Requirements
9. Non-Functional Requirements
10. Out of Scope (v1)
11. Success Metrics
12. Release Plan
13. Appendices

---

## 1. Executive Summary

### What We Are Building

whisperMeOff for Windows is a native Windows application that delivers instant, private, offline voice-to-text transcription. The user holds a global hotkey, speaks, releases the key, and the transcribed text appears in whatever application they were typing in — automatically pasted, no copy-paste required.

Every computation runs on the user's machine. No audio leaves the device. No internet connection is required after the initial model download. No subscription, no account, no telemetry.

This is a native Windows C# WPF application with DirectML GPU acceleration.

### Why We Are Building It

The Electron version is Windows-only and carries significant runtime overhead. The Windows-native rewrite eliminates the 150MB+ Electron runtime, achieves full GPU acceleration via DirectML, and delivers a first-class Windows experience — proper system tray app, native permissions flow, system-integrated audio capture, uses NAudio for audio capture.

### For Whom

Power users, writers, developers, accessibility users, and privacy-conscious professionals who need fast, local voice input. Anyone who finds cloud dictation too slow, too expensive, or a privacy liability.

### Core Value Proposition

- Instant: speak, release, text appears. No cloud round-trip.
- Private: audio never leaves the machine. Zero network traffic.
- Free: MIT open source, no subscription, no upsell.
- Offline: works on an airplane, in a SCIF, anywhere.
- Accurate: Whisper.net with the small model, Vulkan GPU accelerated.

### Competitive Positioning

| Product | Processing | Cost | Open Source | GPU Backend |
|---------|-----------|------|-------------|-------------|
| **whisperMeOff** | Local only | Free | Yes (MIT) | Vulkan/CUDA (Windows) |
| Willow Voice | Cloud (default) | Subscription | No | Cloud |
| SuperWhisper | Local | One-time purchase | No | Metal |

whisperMeOff is the only fully free, fully open-source option in this space. Against SuperWhisper specifically: same local Metal processing, same whisper.cpp foundation, but whisperMeOff bundles the medium model out of the box, costs nothing, and is fully inspectable source code.

---

## 2. Product Vision

### Mission Statement

Give anyone the ability to speak and have their words appear instantly in any application, without surrendering their audio to a server, paying a subscription, or installing anything beyond a single .app.

### Design Principles

**Local-first.** Every feature either works offline or does not ship. This is not negotiable. If a feature requires a network call, it is out of scope for v1.

**Lightweight.** The app itself (excluding models) should be small and fast. Sub-1-second launch. Near-zero CPU at idle. Single .app bundle, no daemons, no kernel extensions, no system modifications.

**Instant.** The gap between releasing the hotkey and text appearing in the target app should feel immediate. Target is under 2 seconds for typical dictation. Every millisecond of unnecessary latency is a defect.

**Transparent.** Open source. Settings are plain JSON. Database is standard SQLite. Users can inspect everything. Nothing is hidden.

**Focused.** whisperMeOff does one thing: voice-to-text with auto-paste. It does not transcribe meetings, summarize documents, take commands, or integrate with cloud services. Scope discipline is a feature.

### What We Are NOT Building

The following are explicitly excluded from v1 and should not be added without explicit roadmap approval:

- Cloud transcription fallback or any optional cloud mode
- Real-time streaming transcription (we batch-process after recording stops)
- Voice commands or wake-word activation
- Custom LLM prompts (the formatting prompt is fixed)
- Mac version
- Electron version maintenance
- App Store distribution
- Subscription billing or payment infrastructure
- User accounts or identity
- Cross-device sync
- Meeting transcription or always-on recording
- Speaker diarization
- Shortcuts.app action integration
- Safari or browser extensions
- Custom vocabulary / word lists (SuperWhisper feature, future consideration)

---

## 3. User Personas

### Persona 1 — Power User (Primary)

**Who:** Developer, technical writer, knowledge worker. Uses a Windows PC all day. Types constantly. Writes code, documentation, emails, Slack messages.

**Pain:** Typing is a bottleneck for their output. Dictation tools are either cloud-dependent (privacy concern), too slow (cloud latency), or clunky (toggle-mode, no auto-paste).

**What they want:** Hold a key, speak fast, text appears. Zero friction. Stays in their flow. Works at the command line, in VS Code, in Slack, in a browser — everywhere.

**How they use whisperMeOff:** Hotkey bound to their preferred combo. Push-to-talk dozens of times per day. Whisper medium model. Llama formatting probably off (they want raw, fast output). Auto-paste always on.

**Success for them:** They forget they installed it. It just works.

### Persona 2 — Accessibility User

**Who:** Person with RSI (repetitive strain injury), carpal tunnel, mobility limitations, or other condition that makes extended typing painful or impossible.

**Pain:** Keyboard use is physically harmful or limited. Cloud dictation requires an internet connection. Existing tools are not reliable enough for professional use.

**What they want:** Reliable transcription that works without typing. Accurate enough to reduce correction time. Works offline (they may be on a hospital network with restricted internet). Accessible settings UI.

**How they use whisperMeOff:** Primary input method, not a convenience tool. Push-to-talk with a hardware shortcut or foot pedal mapped to the hotkey. Llama formatting enabled to add punctuation to natural speech. History used to recover past text.

**Success for them:** They can work a full day without pain.

### Persona 3 — Privacy-Conscious Professional

**Who:** Lawyer, doctor, financial professional, security researcher, government employee. Works with confidential information that cannot leave their machine.

**Pain:** Cloud voice-to-text is a compliance liability. Uploading client conversations, medical records, or legal strategy to any third party is unacceptable. Existing offline tools are hard to set up or technically unreliable.

**What they want:** Mathematically verifiable local processing. No network traffic ever. Auditability (open source). Professional-quality accuracy.

**How they use whisperMeOff:** Dictating notes, reports, correspondence. Will verify zero network traffic with Little Snitch or similar. Will read the source code or have it reviewed. Models stored on encrypted local drive.

**Success for them:** Their compliance officer approves it after reviewing the code. Zero network calls confirmed.

---

## 4. Core User Workflows

### Workflow 1 — First Launch Experience

This is the highest-stakes UX moment. A new user who hits a wall here will never come back.

**Step 1: App opens for the first time.**
The main window appears. The menu bar icon is visible. The window checks for required permissions and displays their status.

**Step 2: Microphone permission check.**
The app checks `AVCaptureDevice.authorizationStatus(for: .audio)`. If not determined, it immediately requests permission via `AVCaptureDevice.requestAccess(for: .audio)`. A native macOS dialog appears. User grants or denies.

If denied: the app shows an inline error in the main window with a "Open System Settings" button that navigates the user to Privacy & Security > Microphone. Recording is disabled until permission is granted.

**Step 3: Accessibility permission check.**
The app checks `AXIsProcessTrusted()`. If false, it shows an inline warning: "Accessibility permission is required for the global hotkey. Grant it in System Settings > Privacy & Security > Accessibility." A button opens System Settings directly to the correct pane.

Without Accessibility permission, the hotkey will not work. The recording button in the main window still works as a fallback.

**Step 4: Model check.**
The app verifies that `%APPDATA%\whisperMeOff\models\whisper\ggml-small.bin` exists and is valid (checks file size, not CRC for speed). If found, status shows: "Whisper ready — medium model."

If the file is missing (should not happen — it is bundled), the app prompts the user to re-copy from the bundle or download. This is a fallback for edge cases only.

**Step 5: Default hotkey registration.**
The app attempts to register the global hotkey with the default hotkey (Ctrl+Shift+R). If Accessibility permission is not yet granted, the hotkey registration is deferred. The app shows the current hotkey in the main window status area.

**Step 6: Ready state.**
Main window displays:
- Status indicator: green dot, "Ready"
- Current hotkey displayed (e.g., "Cmd + Shift + R")
- Whisper model status: "medium — ready"
- Llama status: "Off" (default)
- Waveform canvas (inactive, shows flat line)
- VU meter (inactive, zero level)

User is ready to record.

---

### Workflow 2 — Push-to-Talk Recording Flow

This is the core workflow. It must be fast, reliable, and invisible.

**Step 1: User presses hotkey (Ctrl+Shift+R by default).**
CGEvent tap intercepts the keydown event system-wide. The main process receives the event. The event is consumed (not forwarded to the active application, which would type "r" in the target app).

**Step 2: Audio capture begins.**
`AVAudioEngine` starts capturing from the selected microphone (or default if none selected). Audio format: 16kHz, mono, PCM 16-bit (captured directly in this format — no conversion step needed). Echo cancellation: OFF. Noise suppression: OFF. Auto-gain: OFF.

**Step 3: Recording overlay appears.**
A frameless WPF Window slides into position at the bottom-center of the primary display. It shows: red blinking dot + "Recording" text on a dark translucent background. The window is always-on-top, ignores mouse events, and does not take focus.

**Step 4: Live feedback in main window (if visible).**
If the main window is open, the waveform visualizer (frequency spectrum via Core Audio FFT) animates in real time. The VU meter updates with RMS audio level (green < 30%, yellow 30-60%, orange 60-80%, red > 80%).

**Step 5: User releases hotkey.**
CGEvent tap intercepts the keyup event for the hotkey combination. The event is consumed.

**Step 6: Audio capture stops.**
`AVAudioEngine` stops. The audio buffer is finalized. A temporary WAV file is written to `NSTemporaryDirectory()` (e.g., `whisper_1709876543210.wav`).

**Step 7: Recording overlay hides.**
The NSPanel is hidden immediately.

**Step 8: Whisper transcription.**
On a background thread (not main), whisper.cpp is called with the WAV file path, current model path, language setting, thread count (8), and GPU acceleration enabled (Metal backend). This is a synchronous call on the background thread.

**Step 9: Optional Llama formatting.**
If Llama is enabled and a model is loaded, the raw Whisper text is passed to llama.cpp. The fixed prompt is used (see Section 5.3 for exact text). Output limit: 256 tokens, temperature: 0.1. If Llama fails for any reason, the raw Whisper text is used as fallback — never surface an error to the user for this step.

**Step 10: Text to clipboard.**
The final text (formatted or raw) is written to `NSPasteboard.general`.

**Step 11: Auto-paste.**
`CGEvent` creates a synthetic Ctrl+V keydown followed by keyup. These events are posted to the application that held focus before the hotkey was pressed. The text appears in the target application.

**Step 12: Transcription saved to database.**
The text, timestamp, duration (from recording start to transcription complete), model name, and detected language are written to the SQLite database via GRDB. This happens on a background thread after paste is complete.

**Step 13: Main window update (if visible).**
If the main window is open, the last transcription text is displayed in a status area. No action required from the user.

Total time from hotkey release to text appearing in target: target < 2 seconds on M1 with medium model for 30 seconds of audio.

---

### Workflow 3 — Changing Settings

**Step 1:** User clicks the system tray icon or uses Ctrl+, (standard Windows settings shortcut). The main window comes to front with the settings view active. Alternatively, the user opens Settings from the menu bar right-click context menu.

**Step 2:** Settings panel displays five tabs: Audio, Whisper, Llama, General, History.

**Step 3:** User navigates tabs. Each tab's content is described in Section 5.8.

**Step 4:** Settings are saved on change (not on a "Save" button click). Each control saves its value immediately to `settings.json`.

**Step 5:** User dismisses settings by clicking elsewhere, pressing Escape, or using the standard macOS window close button (which hides to tray, not quits).

---

### Workflow 4 — Downloading a New Whisper Model

**Step 1:** User navigates to Settings > Whisper tab.

**Step 2:** The "Download Models" section shows four options: base (~150MB), small (~500MB), medium (~1.5GB), large (~3GB). Each shows its memory footprint.

**Step 3:** User clicks "Download" next to desired model size.

**Step 4:** Progress bar appears with percentage and status message (e.g., "Downloading ggml-small.bin... 47%").

**Step 5:** File downloads to `%APPDATA%\whisperMeOff\models\whisper\ggml-[size].bin`.

**Step 6:** On completion, the model is automatically selected and saved to settings. Status updates to "ggml-small.bin — ready."

**Step 7:** User can also click "Select Model File" to open an NSOpenPanel and choose a .bin file from anywhere on the system. This is saved as an absolute path in settings.

---

### Workflow 5 — Browsing Transcription History

**Step 1:** User navigates to Settings > History tab. The tab loads the most recent 50 transcriptions from the SQLite database, sorted newest first.

**Step 2:** List displays each transcription with: timestamp (localized date/time), first 80 characters of text, duration if available, model used, and action buttons (copy, delete).

**Step 3:** User types in the search/filter input. The list filters in real time (client-side, no database query) to show only transcriptions containing the search string (case-insensitive).

**Step 4:** User clicks copy on a history item. The full text of that transcription is written to the clipboard. A brief success indicator ("Copied!") appears.

**Step 5:** User clicks delete on a history item. The item is removed from the list and deleted from the database. No confirmation dialog (matches the Electron version's behavior).

**Step 6:** User clicks "Clear All." A confirmation sheet (native macOS) asks "Delete all [N] transcriptions? This cannot be undone." If confirmed, all records are deleted.

---

### Workflow 6 — Configuring the Hotkey

**Step 1:** User navigates to Settings > General tab.

**Step 2:** The hotkey configuration section shows the fixed prefix "Cmd + Shift +" and a single-character input field showing the current key (default: "R").

**Step 3:** User clicks the input field or types a single character (A-Z, 0-9). The field accepts only single alphanumeric characters.

**Step 4:** The new hotkey is registered immediately: the old CGEvent tap listener is removed and a new one is installed for the new combination.

**Step 5:** If the new hotkey conflicts with a system shortcut or cannot be registered, an inline error is shown and the previous hotkey is restored.

**Step 6:** The new hotkey is saved to `settings.json`. The hotkey display in the main window status area updates.

---

### Workflow 7 — Selecting a Microphone

**Step 1:** User navigates to Settings > Audio tab.

**Step 2:** The microphone dropdown shows all available audio input devices enumerated via `AVCaptureDevice.DiscoverySession`. Device names are their hardware display names. The current selection is highlighted.

**Step 3:** User selects a different device. The selection is saved to settings immediately.

**Step 4:** On next recording, the new device is used. There is no test button in v1 (future consideration).

---

## 5. Feature Requirements

### 5.1 Push-to-Talk Recording

#### Global Hotkey System

- Default hotkey: Ctrl+Shift+R
- Modifier combination is fixed: Ctrl+Shift (user cannot change the modifier, only the trigger key)
- Configurable trigger key: any single alphanumeric character A-Z or 0-9
- Implementation: `CGEventTap` at `kCGHIDEventTap` level, event type mask covering `keyDown` and `keyUp`
- The tap runs on a dedicated serial dispatch queue, not the main thread
- Hotkey events are consumed (not forwarded to target application)
- When the app does not have Accessibility permission, the hotkey silently does not work. The main window recording button works as a non-global fallback.
- Modifier key tracking: the implementation must correctly detect that Cmd AND Shift are held when the trigger key fires. Track modifier flags from `CGEventFlags`.
- The tap handles both the physical keydown (start recording) and keyup (stop recording) of the trigger key.
- Rapid press/release (< 200ms) should still produce a transcription attempt, not be silently discarded.

#### Audio Capture

- Capture via `AVAudioEngine` with an `AVAudioInputNode` tap
- Target format: 16kHz, mono, PCM 16-bit (configure this as the engine output format; do NOT capture at device native format and convert)
- Audio is accumulated in a buffer (array of PCM frames) during recording
- On stop, the buffer is written to a temporary WAV file with proper WAV header (RIFF format)
- No echo cancellation: `AVAudioSession` (not applicable on macOS — use `AVCaptureDevice` properties) — specifically do NOT apply `kAUSubType_VoiceProcessingIO` audio unit, use standard `kAUSubType_HALOutput`
- No noise suppression: do not apply any `AVAudioUnit` effects chain
- No auto-gain control: set `AVCaptureDevice.activeFormat` without gain normalization; explicitly set input gain to a neutral value if the API allows
- The audio tap installs on `AVAudioInputNode` with a buffer size of 4096 frames at 16kHz (approximately 256ms per callback)
- Audio data from each tap callback is appended to an in-memory buffer (NSMutableData or similar)

#### Real-time Waveform Visualization

- Frequency spectrum display using FFT
- **NOTE:** Implementation uses randomized visual bars for aesthetic effect, not real FFT frequency analysis. The FFT is computed on the audio tap callback data (or a separate analysis branch)
- FFT size: 256 bins
- Smoothing constant: 0.8 (exponential smoothing between frames)
- Display: vertical bars for each frequency bin
- Color gradient: cyan (#00d4ff) at top, purple (#7c3aed) at midpoint, red (#ef4444) at base (matching Electron version aesthetic)
- Canvas: NSView-based with CoreGraphics drawing, or Metal if performance requires
- Renders only while recording; clears to empty state when not recording
- Updates at display refresh rate (via CADisplayLink equivalent or CVDisplayLink)

#### Real-time VU Meter

- RMS-based audio level computation
- Level: 0-100 scale (RMS of the frequency bin buffer, scaled by 1.5, clamped to 100)
- Color thresholds:
  - 0-29: green (#22c55e)
  - 30-59: yellow (#eab308)
  - 60-79: orange (#f97316)
  - 80-100: red (#ef4444)
- Horizontal bar fill with smooth transition (50ms ease-out)
- Scale labels: 0, 25, 50, 75, 100
- Transition: background-color animates over 200ms, width animates over 50ms

#### Recording Overlay

- NSPanel subclass, `NSBorderlessWindowMask`, transparent background
- Size: 216 x 60 points
- Position: bottom-center of primary display, 20 points from bottom of work area (accounts for Dock)
- Background: `rgba(0, 0, 0, 0.7)` with `NSVisualEffectView` (vibrancy material: `.popover` or `.hudWindow`)
- Corner radius: 30 points
- Always-on-top: `panel.level = .screenSaver` or `.popUpMenu`
- Does not appear in Mission Control, Exposé, or the Dock: `panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]`
- Does not accept mouse events: `panel.ignoresMouseEvents = true`
- Does not steal focus: `panel.becomesKeyOnlyIfNeeded = true`
- Content: red blinking circle (14pt diameter, #ef4444, 10pt glow) + "Recording" label (17pt semibold, white)
- Blink animation: opacity 1.0 to 0.3, 1 second period, ease-in-out, infinite loop, using CoreAnimation
- Shows immediately on hotkey down; hides immediately when transcription starts (not when hotkey up, to cover the transcription processing time)
- Actually hides after transcription completes and text is pasted

---

### 5.2 Transcription Engine

#### whisper.cpp Integration

- Library: whisper.cpp compiled as a static library (libwhisper.a) with Metal backend enabled
- Swift bridging via a C header wrapper or a Swift Package that wraps the C API
- GPU backend: Vulkan first (Windows), then CUDA, then CPU fallback
- No GPU: CPU-only mode (no Metal, reduced performance expectation)
- Thread count for CPU work: 8 threads (matches Electron implementation)
- Input format: WAV, 16kHz, mono, PCM 16-bit (written to NSTemporaryDirectory)
- Output: plain text, no timestamps in the output text (use `whisper_full_get_segment_text`)
- The integration calls `whisper_full()` with a `whisper_full_params` struct
- Parameters:
  - `strategy`: WHISPER_SAMPLING_GREEDY
  - `n_threads`: 8
  - `language`: user-selected language code, or NULL for auto-detect
  - `translate`: false by default, true if "translate to English" is enabled
  - `no_timestamps`: true (we do not display timestamps in output)
  - `print_special`: false
  - `print_progress`: false
  - `use_gpu`: true

#### Pre-bundled Model

- Model: ggml-small.bin (~1.5GB)
- Bundled inside the .exe under `Resources\models\whisper\ggml-small.bin`
- On first launch, copy to `%APPDATA%\whisperMeOff\models\whisper\ggml-small.bin`
- Reason for copy: the app may be installed to Program Files (read-only), and we want models to live alongside downloaded models in user-writable space
- The copy is skipped if the file already exists at the destination and the file size matches
- Model is loaded into memory once and held for the lifetime of the app (not reloaded per transcription)
- Model is loaded on a background thread at launch; the app is ready to record immediately but shows "Loading model..." status until load completes

#### Downloadable Models

Source URL pattern: `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-[size].bin`

| Size | Filename | Approx Size | Memory |
|------|----------|-------------|--------|
| tiny | ggml-tiny.bin | ~75MB | ~150MB |
| base | ggml-base.bin | ~150MB | ~300MB |
| small | ggml-small.bin | ~500MB | ~1GB |
| medium | ggml-small.bin | ~1.5GB | ~3GB |
| large | ggml-large.bin | ~3GB | ~6GB |

Download destination: `%APPDATA%\whisperMeOff\models\whisper\`

Download implementation: `URLSession` data task with progress via `URLSessionDownloadDelegate`. No curl subprocess required (unlike the Windows version). Resume support via `URLSessionDownloadTask.resume()`-from-resume-data if download is interrupted.

Minimum valid file size: 1MB (guard against downloading error pages).

After download completes: auto-select the new model, save to settings.

#### Custom Model Selection

- NSOpenPanel filtered to `.bin` files
- Any valid whisper.cpp GGUF-format .bin file from anywhere on the filesystem
- Absolute path saved to settings
- Model is reloaded when path changes

> **NOTE (v1):** Language selection UI is simplified. Only translation toggle is available; full language dropdown not yet implemented.\n\n#### Language Support

Language options (UI display name : language code passed to whisper):
- Auto Detect : `auto` (passes NULL to whisper, let it detect)
- English : `en`
- Spanish : `es`
- French : `fr`
- German : `de`
- Italian : `it`
- Portuguese : `pt`
- Russian : `ru`
- Chinese : `zh`
- Japanese : `ja`
- Korean : `ko`

#### Translate to English

Boolean toggle in Whisper settings. When enabled, the `translate` parameter is set to `true` in `whisper_full_params`. Whisper will transcribe non-English audio and output English text. Default: false.

#### Performance Target

- 30 seconds of audio on M1 with ggml-small.bin: under 2 seconds transcription time
- This is achievable with Metal GPU backend based on whisper.cpp benchmarks

---

### 5.3 Text Formatting (Llama.cpp)

#### Integration

- llama.cpp compiled as a static library (libllama.a) with Metal backend enabled
- Swift bindings via a C header wrapper or swift-llama-cpp package
- Runs on a background thread after Whisper completes
- Metal GPU backend enabled on Apple Silicon (same as whisper.cpp)
- Model format: GGUF (`.gguf` files)
- Model is loaded once and held in memory while enabled; unloaded when disabled

#### Toggle Behavior

- Default: disabled
- When enabled and model is loaded: formatting runs after every transcription
- When disabled: raw Whisper output is used
- When enabled but no model is configured: show inline warning "No Llama model selected." Raw output is used.
- Toggle state is saved to settings immediately

#### Exact Prompt Template

The prompt sent to llama.cpp is fixed and not user-configurable:

```
Convert any file paths in this text to proper format. Output only the result, nothing else.

Text: [RAW WHISPER TEXT]

Result:
```

Where `[RAW WHISPER TEXT]` is replaced with the actual Whisper output. The model continues generating after "Result:" and that continuation is the output.

#### Generation Parameters

- `n_predict` (max tokens): 256
- `temp` (temperature): 0.1
- `repeat_penalty`: 1.1 (standard, prevents repetition)
- `n_threads`: 4 (CPU threads for llama, less than whisper since Metal handles GPU)
- Stop sequences: none specified (rely on 256 token limit)

#### Fallback Behavior

If llama.cpp processing fails for any reason (timeout, error, crash, model not loaded):
1. Log the error internally
2. Use the raw Whisper output as the final text
3. Do NOT surface an error message to the user for this step
4. The paste still happens with the raw text

This ensures the core workflow (record → transcribe → paste) is never blocked by an optional component.

#### Model Management

Llama models are stored in: `%APPDATA%\whisperMeOff\models\llama\`

Users can:
1. Select a local .gguf file via NSOpenPanel
2. Download by entering a HuggingFace model ID (see Section 5.9)

#### Pre-configured Download Option

The app includes one pre-configured Llama model option (matching the Electron version):

| ID | Name | Size | Memory | Description |
|----|------|------|--------|-------------|
| qwen2.5-0.5b | Qwen2.5-0.5B Instruct | ~370MB | ~1GB RAM | Fast, low memory, great for formatting |

Download URL: `https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf`

---

### 5.4 Auto-Paste System

#### Previous Window Tracking

On macOS, the approach is:
1. When the CGEvent tap detects the hotkey keydown, the system records which application was frontmost at that moment using `NSWorkspace.shared.frontmostApplication`
2. Store the `bundleIdentifier` and `processIdentifier` of that application
3. After transcription completes, this is the target for the Cmd+V event

#### Previous Window Tracking — Edge Cases

- If whisperMeOff was itself the frontmost app when the hotkey was pressed: do not paste into whisperMeOff. In this case, skip the paste step. Text is still on the clipboard.
- If the previously focused application no longer exists by the time we paste: skip the paste step gracefully.
- If there was no previous application (whisperMeOff is the only app): skip the paste step.

#### Clipboard Write

- `NSPasteboard.general.clearContents()`
- `NSPasteboard.general.setString(text, forType: .string)`
- This happens before the Cmd+V simulation

#### Cmd+V Simulation

```swift
// Restore focus to the target application
NSWorkspace.shared.launchApplication(withBundleIdentifier: targetBundleID,
    options: [], additionalEventParamDescriptor: nil, launchIdentifier: nil)

// Small delay to ensure the app is focused (50ms)
// Then synthesize Cmd+V:
let source = CGEventSource(stateID: .hidSystemState)
let vKeyCode = CGKeyCode(9)  // kVK_ANSI_V

let cmdV_down = CGEvent(keyboardEventSource: source, virtualKey: vKeyCode, keyDown: true)
cmdV_down?.flags = .maskCommand
cmdV_down?.post(tap: .cgAnnotatedSessionEventTap)

let cmdV_up = CGEvent(keyboardEventSource: source, virtualKey: vKeyCode, keyDown: false)
cmdV_up?.flags = .maskCommand
cmdV_up?.post(tap: .cgAnnotatedSessionEventTap)
```

#### Previous Clipboard Preservation

Before writing transcription to clipboard:
1. Save the current clipboard contents (if they are plain text)
2. Write the transcription text
3. After paste event is delivered (100ms delay): restore the previous clipboard contents

This is a best-effort behavior. If the previous clipboard had non-text content (image, file reference), it is not restored (only plain text is preserved). Document this limitation.

#### Focus Management

The main window behavior during recording:
- Track whether the main window was visible when the hotkey was pressed (`mainWindowWasVisible` flag)
- If the main window was NOT visible when the hotkey was pressed: keep it hidden after transcription completes
- If the main window WAS visible: it remains visible (do not hide it)
- The recording overlay appears regardless of main window visibility

---

### 5.5 System Tray App

#### NotifyIcon (WPF)

- `System.Windows.Forms.NotifyIcon`
- Icon: a custom icon ( ICO format)
- Fallback icon: use a microphone icon from embedded resources
- The app appears in the system tray (notification area)
- The notify icon persists for the lifetime of the app

#### Click Behavior

- Single left-click: toggle main window visibility (show if hidden, hide if visible)
- Right-click: show context menu

#### Context Menu

```
Show whisperMeOff       (or "Hide whisperMeOff" if window is visible)
Settings                (navigates to Settings tab in main window)
──────────────────
Exit whisperMeOff
```

- "Settings" opens the main window with the Settings panel active (same as Ctrl+,)
- "Exit" terminates the app completely (saves all state first)

#### App Lifecycle

- Closing the main window hides the window (does not quit)
- The app continues running with the system tray icon and hotkey active
- Quitting only via "Exit" in the context menu, or Alt+F4 when the main window is front

---

### 5.6 Recording Overlay\n\n> **NOTE (v1):** Implementation uses VuMeterWindow with waveform visualization. The recording overlay spec (red blinking dot + Recording text) is not yet implemented.

Full specification described in Section 5.1 under "Recording Overlay." Summary:

- 216 x 60 points, bottom-center of primary display, 20 pts from bottom edge of work area
- Frameless, transparent, always-on-top NSPanel
- Dark translucent background (NSVisualEffectView, .popover or .hudWindow material)
- Red blinking dot (14pt, #ef4444) + "Recording" text (17pt semibold, white)
- Blink: opacity 1.0 to 0.3, 1 second period, ease-in-out, infinite
- Ignores mouse events, does not steal focus
- Visible only during active recording AND transcription processing
- Hidden immediately after transcription result is ready and paste is dispatched

---

### 5.7 Transcription History

#### Database

- SQLite via Microsoft.Data.Sqlite
- Database file: `%APPDATA%\whisperMeOff\transcriptions.db`
- The database is opened once at app launch and kept open for the app lifetime
- Writes happen on a background thread; reads for the UI happen asynchronously

#### Schema

```sql
CREATE TABLE transcriptions (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    text      TEXT    NOT NULL,
    timestamp TEXT    NOT NULL,   -- ISO 8601, e.g. "2026-03-08T14:32:11Z"
    duration  REAL,               -- seconds, nullable (time from rec start to transcription complete)
    model     TEXT,               -- e.g. "ggml-small.bin", nullable
    language  TEXT                -- e.g. "en", "auto", nullable
);
```

#### CRUD Operations

| Operation | Behavior |
|-----------|----------|
| Create | INSERT with text, timestamp (now), duration, model, language |
| Read All | SELECT ... ORDER BY timestamp DESC LIMIT 50 |
| Read One | SELECT ... WHERE id = ? |
| Delete One | DELETE WHERE id = ? |
| Delete All | DELETE FROM transcriptions (no WHERE) |

#### UI — History Tab

- Search/filter text input at the top (full-width)
- Filtering is client-side: all 50 records are loaded; filter by `text.lowercased().contains(query.lowercased())`
- For queries beyond 50 records: add "Load More" button in a future version
- Each list item displays:
  - Timestamp: localized date and time (e.g., "Mar 8, 2026 at 2:32 PM")
  - Model and language if available (subtle secondary text)
  - Duration if available (e.g., "12.3s")
  - Text content (truncated to 3 lines, expandable on click in future version)
  - Copy button: copies full text to clipboard, shows "Copied!" briefly
  - Delete button: removes item from list and database, no confirmation
- "Clear All" button at bottom of list: shows confirmation sheet first
- Confirmation text: "Delete all [N] transcriptions? This cannot be undone." Buttons: "Cancel" (default) and "Delete All" (destructive)
- Sort: newest first (fixed, no sort options in v1)
- Empty state: "No transcriptions yet. Hold [hotkey] to record."
- No-match state (search with no results): "No transcriptions match your search."

---

### 5.8 Settings

Settings are stored in `%APPDATA%\whisperMeOff\settings.json`. The settings file is read at launch and written on every change. It is plain JSON, human-readable, and directly editable by advanced users.

Settings UI is implemented as a tabbed panel within the main window (not a separate window). Tabs are: Audio, Whisper, Llama, General, History.

#### Audio Tab

**Microphone Selection**
- Type: dropdown (NSPopUpButton)
- Options: "Default Microphone" + all available AVCaptureDevice input devices
- Device enumeration: `AVCaptureDevice.DiscoverySession(deviceTypes: [.microphone], mediaType: .audio, position: .unspecified).devices`
- Display name: `device.localizedName`
- Stored value: `device.uniqueID` (stable across sessions)
- Default: "" (empty string = system default)
- Behavior on change: saved immediately to settings; takes effect on next recording
- The currently active device is shown with its human-readable name

#### Whisper Tab

**Language Selection**
- Type: dropdown (NSPopUpButton)
- Options: see language list in Section 5.2
- Default: "auto"
- Stored value: language code string
- Behavior: saved immediately; takes effect on next transcription

**Translate to English**
- Type: checkbox (NSButton checkboxStyle)
- Default: false (unchecked)
- Stored value: boolean
- Behavior: saved immediately

**Model Status**
- Read-only text field showing current model file path (truncated to filename if too long)
- Status indicator: green if file exists and is valid, red if not found

**Select Model File Button**
- Opens NSOpenPanel with filter for `.bin` files
- Default directory: `%APPDATA%\whisperMeOff\models\whisper\`
- On selection: updates model path in settings, triggers model reload

**Download Models Section**
- Title: "Download Whisper Models"
- One row per model size (tiny, base, small, medium, large):
  - Model name (e.g., "medium")
  - File size estimate (e.g., "~1.5GB")
  - Memory estimate (e.g., "~3GB VRAM")
  - "Download" button (disabled and shows checkmark if already downloaded)
  - Per-model progress bar (appears during download of that specific model)
- Downloading one model does not prevent switching to another already-downloaded model

#### Llama Tab

**Enable Llama Text Formatting**
- Type: checkbox
- Default: false (unchecked)
- Stored value: boolean (`llama.enabled`)
- Behavior: saved immediately; disabling unloads the model from memory; enabling loads the model if one is selected

**Llama Library Status**
- Read-only indicator: "Loaded" (since llama.cpp is compiled in, it is always available)
- Distinct from model status

**Llama Model Path**
- Read-only text field showing current model file path (truncated to filename)
- Status: green if file exists, amber if enabled but no model, grey if disabled

**Select Model File Button**
- Opens NSOpenPanel with filter for `.gguf` files (also accepts all files as fallback)
- On selection: saves path to settings, loads model if Llama is enabled

**HuggingFace Model ID Input**
- Type: text field (NSTextField)
- Placeholder: `e.g., ggml-org/tinygemma3-GGUF:Q8_0`
- Format: `[owner/repo-name]` or `[owner/repo-name]:[quantization-suffix]`
- Stored value: `llama.modelId`
- Save behavior: on field blur (focus lost)
- Tooltip/hint: "Find models at huggingface.co/models — search for GGUF quantized models"

**Download HuggingFace Model Button**
- Disabled when: model ID input is empty OR a download is in progress
- Behavior: initiates the HuggingFace GGUF discovery and download process (see Section 5.9)

**Download Progress**
- Shows when download is in progress or just completed
- Components: status message text, progress bar, percentage label
- Status states: "Searching for model files...", "Found [filename], starting download...", "Downloading [filename]...", "Downloaded [filename] successfully!", "Error: [message]"
- Progress bar: green during download, full green on complete, red on error

**HuggingFace Token**
- Type: secure text field (NSSecureTextField, input is masked)
- Placeholder: `hf_xxxxxxxxxxxxx`
- Hint: "Required for gated/private models. Get yours at huggingface.co/settings/tokens"
- Stored value: `llama.huggingFaceToken`
- Save behavior: on field blur
- The token is stored in plain text in settings.json (not Keychain in v1 — future security improvement)

#### General Tab

**Hotkey Configuration**
- Label: "Push-to-Talk Hotkey"
- Fixed prefix display: "Cmd + Shift +"
- Single character input (NSTextField, maxLength: 1)
- Only accepts alphanumeric characters A-Z and 0-9 (other input rejected)
- Current value displayed in the field
- Behavior: on input of a valid character, immediately register the new hotkey and save to settings
- Validation: if registration fails, revert to previous key and show inline error
- Hint text: "Type a single letter or number. The Ctrl+Shift modifiers are fixed."

**Launch at Login**
- Type: checkbox
- Default: false
- Implementation: `SMAppService.mainApp.register()` / `.unregister()` (macOS 13+)
- Stored value: boolean (but actual state is managed by SMAppService, not just the JSON)

#### History Tab

The History tab is the full transcription history UI as described in Section 5.7. It is a tab within the settings panel, not a separate screen.

---

### 5.9 Model Management

#### Whisper Model Download

- Source: HuggingFace ggerganov/whisper.cpp repository
- URL pattern: `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-[size].bin`
- Implementation: `URLSession` with `URLSessionDownloadDelegate`
- Progress: `urlSession(_:downloadTask:didWriteData:totalBytesWritten:totalBytesExpectedToWrite:)` — percentage = `bytesWritten / totalBytes * 100`
- Destination: `%APPDATA%\whisperMeOff\models\whisper\ggml-[size].bin`
- Minimum valid file size: 1MB (reject smaller files as error pages)
- On completion: auto-select the new model, save settings, update UI

#### Llama GGUF Download

The HuggingFace download flow handles the case where the user provides only a model ID (not a direct URL):

**Step 1: Parse the model ID.**
- If input contains `://` it is a full URL — use directly
- If input contains `:` split on `:` — left side is `[owner/repo]`, right side is quantization hint (e.g., `Q8_0`)
- Otherwise treat as `[owner/repo]`

**Step 2: Discover the GGUF file URL.**
Try these strategies in order:

Strategy A — Pattern matching. Try common filename patterns:
```
[repo-name-lowercase]-[quantization-lowercase].gguf
[repo-name-lowercase]-Q4_K_M.gguf
[repo-name-lowercase]-q4_k_m.gguf
[repo-name-lowercase]-q5_k_m.gguf
[repo-name-lowercase]-q8_0.gguf
[repo-name-lowercase].gguf
model-q4_k_m.gguf
model.gguf
```
For each: send a HEAD request to `https://huggingface.co/[owner/repo]/resolve/main/[filename]`. First 200 response wins.

Strategy B — HuggingFace API. Call `https://huggingface.co/api/models/[owner/repo]/tree/main`. Parse the JSON array for entries where `type == "file"` and `path` ends with `.gguf`. Use the first result.

If a HuggingFace token is set, include it as `Authorization: Bearer [token]` header on all requests.

If both strategies fail, show error: "Could not find a GGUF file for this model. Try entering the full download URL."

**Step 3: Download.**
Same URLSession mechanism as Whisper download. Save to `%APPDATA%\whisperMeOff\models\llama\[filename]`. Auto-select on completion.

#### File System Layout

```
%APPDATA%\whisperMeOff\
├── settings.json
├── transcriptions.db
└── models/
    ├── whisper/
    │   ├── ggml-small.bin     (pre-bundled, copied on first launch)
    │   ├── ggml-base.bin       (downloaded on demand)
    │   ├── ggml-small.bin      (downloaded on demand)
    │   └── ggml-large.bin      (downloaded on demand)
    └── llama/
        └── [user-downloaded GGUF files]
```

Temporary files (WAV audio for transcription): `NSTemporaryDirectory()/whisper_[timestamp].wav`. Deleted after transcription completes.

---

### 5.10 Onboarding / First Launch

On first launch (detected by absence of `settings.json`):

1. Create the application support directory if it does not exist
2. Copy `ggml-small.bin` from the app bundle `Resources/models/whisper/` to the user support directory (async, on a background thread)
3. Write default `settings.json`
4. Check Microphone permission — request if not determined
5. Check Accessibility permission — show guidance if not granted
6. Show "Welcome" state in main window: brief one-paragraph explanation of what the app does and how to use the hotkey

On subsequent launches:
1. Load `settings.json`
2. Verify model file still exists at saved path
3. Check permissions (inform user if revoked, do not prompt)
4. Register CGEvent tap
5. Load Whisper model into memory (background)
6. Load Llama model if enabled (background)
7. Show ready state

---

## 6. Technical Architecture

### 6.1 Technology Stack

| Component | Technology | Notes |
|-----------|-----------|-------|
| Language | C# 12+ (.NET 8) | Primary language |
| UI Framework | WPF (Windows Presentation Foundation) | Native Windows UI |
| Audio Capture | NAudio (Core Audio API) | uses NAudio for audio capture |
| Audio Analysis | MathNet.Numerics | FFT for waveform visualization |
| Transcription | whisper.cpp (libwhisper) | DirectML backend, statically linked |
| Text Processing | llama.cpp (libllama) | DirectML backend, statically linked |
| Database | Microsoft.Data.Sqlite | SQLite for .NET |
| Global Hotkeys | Windows API (RegisterHotKey) | Requires no special permissions |
| Auto-Paste | Windows API (SendInput) | Ctrl+V synthesis |
| System Tray | NotifyIcon (WPF) | |
| Overlay | WPF Window | Frameless, transparent |
| Networking | HttpClient | Only for model downloads |
| Settings | JSON (System.Text.Json) | Plain file in AppData |
| Distribution | MSI or EXE (Inno Setup) | Code signed, direct download |
| Min Windows | Windows 10 1903+ | For DirectML support |
| Architecture | x64 primary | Windows 64-bit |

### 6.2 Process Model

whisperMeOff is a single-process application. There is no Electron multi-process architecture to replicate.

Thread model:
- **Main thread**: UI updates, WPF Dispatcher, Clipboard writes
- **Audio thread**: NAudio callback, FFT computation, level metering
- **Transcription thread**: whisper.cpp inference (dedicated Task, Priority: High)
- **Llama thread**: llama.cpp inference (dedicated Task, Priority: High)
- **Database thread**: SQLite connection (async, managed by connection pool)
- **Global hotkey callback thread**: RegisterHotKey callback runs on a dedicated thread
- **Download thread**: HttpClient handler (managed by .NET)

No shared mutable state between threads without synchronization. Use async/await, lock statements, or ConcurrentDictionary appropriately.

### 6.3 Data Flow

Complete data flow from hotkey press to text paste:

```
1. CGEventTap callback fires on tap's CFRunLoop thread
   → Detect hotkey keydown (Cmd+Shift+triggerKey)
   → Record frontmostApplication (NSWorkspace)
   → Consume the event (return NULL to prevent forwarding)
   → Dispatch to main thread: notify UI that recording has started

2. Main thread: Recording started
   → Show recording overlay (NSPanel.orderFront)
   → Update main window status (if visible)
   → Dispatch async to audio thread: startRecording()

3. Audio thread: AVAudioEngine.start()
   → Install tap on inputNode
   → Each tap callback:
     a. Append PCM frames to buffer (NSMutableData)
     b. Compute FFT (vDSP) → dispatch to main thread for waveform update
     c. Compute RMS level → dispatch to main thread for VU meter update

4. CGEventTap callback fires: hotkey keyup detected
   → Consume the event
   → Dispatch to main thread: notify UI that recording has stopped

5. Main thread: Recording stopped
   → Dispatch to audio thread: stopRecording()

6. Audio thread: stopRecording()
   → Stop AVAudioEngine
   → Finalize audio buffer
   → Write WAV file to NSTemporaryDirectory()
   → Dispatch to transcription thread: transcribeFile(wavPath)

7. Transcription thread: whisper.cpp inference
   → whisper_full() with Metal backend
   → Extract text from all segments: whisper_full_get_segment_text()
   → Dispatch to Llama thread (if enabled) OR main thread (if disabled)

8a. Llama thread (if enabled): llama.cpp inference
    → Build prompt with raw Whisper text
    → llama_eval() with 256 token limit, temp 0.1
    → Extract formatted text
    → Dispatch to main thread: transcriptionComplete(formattedText)
    → If any error: dispatch raw Whisper text instead

8b. Main thread (if Llama disabled): transcriptionComplete(rawText)

9. Main thread: transcriptionComplete(text)
   → Write text to NSPasteboard.general
   → Post CGEvent Cmd+V to previous application
   → Hide recording overlay
   → Update main window with last transcription (if visible)
   → Dispatch to database thread: logTranscription(text, duration, model, language)
   → Schedule clipboard restoration (100ms delay) → restore previous clipboard

10. Database thread: GRDB INSERT
    → Write record to transcriptions.db
    → No UI update needed (history tab loads fresh on open)

11. Temp file cleanup
    → Delete WAV file from NSTemporaryDirectory()
```

### 6.4 CGEvent Tap Implementation

```swift
// Create event tap at HID level to intercept before apps receive events
let eventMask: CGEventMask = (1 << CGEventType.keyDown.rawValue) | (1 << CGEventType.keyUp.rawValue)

let eventTap = CGEvent.tapCreate(
    tap: .cgHIDEventTap,
    place: .headInsertEventTap,
    options: .defaultTap,
    eventsOfInterest: eventMask,
    callback: hotkeyCallback,
    userInfo: Unmanaged.passRetained(self).toOpaque()
)

// Install in a dedicated run loop
let runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, eventTap, 0)
CFRunLoopAddSource(tapRunLoop, runLoopSource, .commonModes)
CGEvent.tapEnable(tap: eventTap, enable: true)
```

The callback checks `CGEventFlags` for `.maskCommand` and `.maskShift`, and the virtual keycode matches the configured trigger key.

### 6.5 whisper.cpp C API Bridge

Swift cannot call C++ directly without a bridging layer. Two options:

**Option A (recommended): Objective-C++ wrapper**
Create `WhisperBridge.mm` (Objective-C++) that:
- Includes `whisper.h`
- Exposes a Swift-callable Objective-C interface
- Manages the `whisper_context` lifecycle
- Calls `whisper_init_from_file()`, `whisper_full()`, `whisper_full_get_segment_text()`, `whisper_free()`

**Option B: C wrapper**
Create `whisper_c_bridge.h` / `whisper_c_bridge.c` that exposes a pure C API wrapping the C++ whisper API. Import via module map.

Either approach is valid; the technical architect makes the final call.

### 6.6 File System Layout

```
whisperMeOff.app/
└── Contents/
    ├── Info.plist
    ├── MacOS/
    │   └── whisperMeOff          (executable)
    ├── Frameworks/               (any dynamic frameworks, if any)
    └── Resources/
        ├── Assets.xcassets       (icons, images)
        ├── AppIcon.icns
        └── models/
            └── whisper/
                └── ggml-small.bin   (~1.5GB — bundled)

~/Library/Application Support/whisperMeOff/
├── settings.json
├── transcriptions.db
└── models/
    ├── whisper/
    │   └── ggml-small.bin   (copied from bundle on first launch)
    └── llama/
        └── (user-downloaded GGUF files)

NSTemporaryDirectory()/
└── whisper_[timestamp].wav   (ephemeral, deleted after transcription)
```

---

## 7. Windows Permission Requirements

### 7.1 Microphone Access

- Permission type: Windows Microphone
- API: `MMDeviceEnumerator` from Core Audio API
- Request timing: on first launch, before the user attempts to record
- API: `MMDeviceEnumerator.RequestAuthorization()`
- Fallback if denied: disable recording, show persistent inline warning with "Open Settings" button
- Windows Settings path: Privacy > Microphone

### 7.2 Global Hotkey Access

- Windows does not require special permissions for global hotkeys via `RegisterHotKey`
- The app uses Windows API `RegisterHotKey` for global hotkey registration
- No special permissions required - works out of the box on Windows
- Fallback: if hotkey registration fails, show inline warning

### 7.3 Input Accessibility

- Windows does not require special permissions for basic hotkey functionality
- For accessibility features (like simulating keyboard input), the app uses `SendInput` API
- No special permissions required
- Document: Works on Windows 10/11 without additional configuration

### 7.4 Permission Handling Flow

```
Launch
  ↓
Check microphone permission
  → Not determined: request permission
  → Denied: show warning, disable recording
  → Authorized: continue
  ↓
Register global hotkey
  → If fails: show warning, show in-window record button
  → If succeeds: ready for global hotkey
  ↓
Ready
```

The permission warnings are shown inline in the main window, not as modal dialogs. They are dismissible but reappear if the user reopens the app without granting the permissions.

---

## 8. Performance Requirements

| Metric | Target | Condition |
|--------|--------|-----------|
| Transcription latency | < 2 seconds | 30s audio, ggml-small, Windows, Vulkan GPU |
| App launch to ready | < 1 second | Model loads async; "loading" state shown |
| Recording start (hotkey → audio) | < 100ms | From hotkey down to first audio sample |
| Hotkey response | < 50ms | From physical keypress to recording overlay appearing |
| Memory footprint (idle) | < 100MB | App only, no model loaded |
| Memory footprint (medium model) | < 3.5GB | whisper.cpp medium model loaded in memory |
| Memory footprint (medium + llama) | < 4.5GB | Both models loaded |
| CPU at idle | < 1% | No recording in progress |
| CPU during recording | < 5% | Audio capture only, no inference |
| CPU during transcription | < 100% on all P-cores | Inference is single-shot, brief |
| App bundle size (no model) | < 50MB | Executable + resources, excluding model |
| App bundle size (with model) | < 1.7GB | Including ggml-small.bin |

---

## 9. Non-Functional Requirements

### Privacy

Zero network calls during normal operation. The only outbound network traffic is:
- Model downloads (user-initiated, to HuggingFace CDN)
- HuggingFace API HEAD requests during model discovery (user-initiated)

There is no telemetry, no analytics, no crash reporting that phones home, no update checker, no license validation. Verification: run the app for 30 minutes with Little Snitch blocking all outbound; it must work perfectly.

### Offline Operation

After models are downloaded, the app requires no internet access. All features must work completely offline. The download manager gracefully handles no-network conditions with an appropriate error message.

### Security

- The app is code-signed with an Apple Developer certificate
- Hardened Runtime is enabled
- Notarization via `notarytool` (required for distribution outside the App Store without Gatekeeper warnings)
- No `com.apple.security.get-task-allow` entitlement in production builds
- The temporary WAV file is written to the user's own temp directory, not a shared location
- The settings.json contains the HuggingFace token in plain text — v1 accepts this; v2 should move to Keychain
- No network listening; the app makes no inbound connections

### Stability

- Crash-free audio pipeline: audio tap callbacks must never throw exceptions; wrap in do-catch or use result types
- Graceful degradation: if whisper.cpp crashes or returns an error, show an error in the UI but do not crash the app
- If llama.cpp fails, use raw Whisper output (see Section 5.3 fallback)
- Database writes are atomic (GRDB handles transactions)
- App recovery: on next launch after a crash, the database is intact (GRDB uses WAL mode)

### Portability

- No kernel extensions (kexts)
- No system daemons or launch agents
- No modifications to system files
- Fully uninstallable via Windows Settings > Apps > Apps & features + deleting `%APPDATA%\whisperMeOff\`

### Accessibility

- All settings UI controls are accessible via VoiceOver
- All buttons have accessible labels (`.accessibilityLabel` set in SwiftUI)
- The recording overlay is marked as an announcement (`.accessibilityAddTraits(.isStaticText)`) so VoiceOver reads "Recording" when it appears
- The main window is keyboard-navigable (Tab through controls)

---

## 10. Out of Scope (v1)

The following are explicitly excluded and should not be implemented in the v1 release:

| Feature | Reason |
|---------|--------|
| Cloud transcription option | Violates local-first principle |
| Real-time streaming transcription | Architectural complexity; batch model is simpler and accurate |
| Voice commands / wake word | Always-on recording is a privacy concern and scope creep |
| Custom LLM prompts | Fixed prompt is sufficient; configurability adds UI complexity |
| iOS / iPadOS version | Different platform entirely |
| Windows version | This IS the Windows version |
| App Store distribution | Sandboxing requirements conflict with CGEvent tap and audio permissions |
| Subscription billing | Not a subscription product |
| User accounts | No server, no accounts |
| Cross-device sync | No server |
| Shortcuts.app integration | Future milestone |
| Custom vocabulary / word lists | Future milestone (SuperWhisper parity) |
| Speaker diarization | Requires different model |
| Meeting transcription (background, always-on) | Scope and privacy risk |
| System-wide audio capture (transcribe other apps' audio) | Requires a different permission model entirely |
| Automatic updates (Sparkle) | v1 will be manual download; add Sparkle in v1.1 |
| Translation to non-English languages | Whisper only supports translate-to-English |

---

## 11. Success Metrics

### Technical

- App launches and reaches ready state within 1 second on M1
- Transcription of 30-second audio completes in under 2 seconds on M1 with ggml-medium
- Zero network packets emitted during 30-minute recording session (verify with Little Snitch)
- Zero crashes in 100 consecutive push-to-talk sessions
- Auto-paste works correctly in: Safari, Chrome, VS Code, Terminal, Slack, Notes, Pages, TextEdit
- Whisper medium model accuracy matches SuperWhisper medium on the same audio (subjective comparison test)

### User Experience

- A new user can go from app download to first successful transcription in under 3 minutes (timing from download complete to text appearing in target app)
- Push-to-talk hotkey works system-wide in all tested apps
- Recording overlay appears within 100ms of hotkey press
- The previous app correctly receives the paste in 95%+ of test cases

### Distribution

- App bundle size (with pre-bundled medium model): under 1.8GB
- DMG size: under 2GB
- Notarization passes without manual review
- Gatekeeper allows opening the app on macOS 13+ without warnings after notarization

---

## 12. Release Plan

### v0.1 — Core Pipeline (Internal)

Goal: prove the fundamental loop works natively on Mac. No UI polish required.

Features:
- NAudio recording (16kHz mono WAV)
- Whisper.net (whisper.cpp .NET bindings), medium model
- CGEvent tap global hotkey (Cmd+Shift+R, not configurable)
- NSPasteboard + CGEvent Cmd+V auto-paste
- Basic NSStatusItem menu bar icon
- Recording overlay (NSPanel)
- Basic main window (start/stop button, status text)

Not included: settings, history, Llama, model download, model selection, audio visualization

Deliverable: a functional app that records and pastes transcriptions. Used by the development team for daily dogfooding.

---

### v0.2 — Settings and History

Features added:
- Settings panel with all tabs: Audio, Whisper, General, History
- Microphone device selection
- Language selection and translate toggle
- Whisper model file selection (NSOpenPanel)
- Downloadable Whisper models (base, small, large, in addition to pre-bundled medium)
- Hotkey configuration (A-Z, 0-9)
- SQLite history (GRDB)
- History tab: view, copy, delete, search, clear all
- Launch at login (SMAppService)
- Waveform visualizer (FFT-based frequency spectrum)
- VU meter (RMS level with color thresholds)
- Permission handling UI (microphone and accessibility warnings)
- First-launch experience

Deliverable: feature-complete for the core workflow. Ready for external beta testing.

---

### v0.3 — Llama Text Formatting

Features added:
- LLamaSharp (llama.cpp .NET bindings), statically linked
- Llama settings tab: enable/disable, model path, status indicator
- Llama model file selection (NSOpenPanel, .gguf)
- Pre-configured Qwen2.5-0.5B download option
- HuggingFace model ID download with GGUF discovery
- HuggingFace API token support
- Download progress UI (progress bar, status messages, percentage)
- Llama model loaded/unloaded with enable/disable toggle
- Fallback to raw Whisper output on Llama failure

Deliverable: full feature parity with the Electron v1.1.0 release, on native Mac.

---

### v1.0 — Release

Polish, testing, and distribution:
- DMG packaging and code signing
- Notarization via notarytool
- Icon (menu bar template image + app icon)
- Error handling review (all edge cases covered, no silent failures)
- Accessibility audit (VoiceOver, keyboard navigation)
- Performance validation (transcription latency on M1, M2, M3, Intel)
- README and first-launch instructions
- GitHub release with notarized DMG attachment

---

## 13. Appendices

### Appendix A — Settings JSON Schema

Complete settings.json schema with all fields, types, and defaults:

```json
{
  "whisper": {
    "modelPath": "",
    "modelSize": "medium",
    "language": "auto",
    "translate": false
  },
  "llama": {
    "enabled": false,
    "modelPath": "",
    "modelId": "",
    "huggingFaceToken": ""
  },
  "audio": {
    "deviceId": ""
  },
  "general": {
    "hotkeyTriggerKey": "r",
    "launchAtLogin": false
  }
}
```

Field definitions:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| whisper.modelPath | string | "" | Absolute path to active Whisper .bin model |
| whisper.modelSize | string | "medium" | Last downloaded/selected model size name |
| whisper.language | string | "auto" | Language code or "auto" for detection |
| whisper.translate | boolean | false | Translate output to English |
| llama.enabled | boolean | false | Enable Llama text formatting |
| llama.modelPath | string | "" | Absolute path to active Llama .gguf model |
| llama.modelId | string | "" | HuggingFace model ID last entered by user |
| llama.huggingFaceToken | string | "" | HuggingFace API token (plain text, v1) |
| audio.deviceId | string | "" | AVCaptureDevice uniqueID, "" = system default |
| general.hotkeyTriggerKey | string | "r" | Single character trigger key (lowercase) |
| general.launchAtLogin | boolean | false | Register with SMAppService |

Note: the hotkey is always Ctrl+Shift+[hotkeyTriggerKey]. The modifiers are not configurable.

---

### Appendix B — Database Schema DDL

```sql
-- whisperMeOff transcriptions database
-- Location: %APPDATA%\whisperMeOff\transcriptions.db
-- Journal mode: WAL (set on open via PRAGMA journal_mode=WAL)

PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS transcriptions (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    text      TEXT    NOT NULL,
    timestamp TEXT    NOT NULL,
    -- ISO 8601 UTC, e.g. "2026-03-08T14:32:11.123Z"
    duration  REAL,
    -- Recording duration in seconds (NULL if not measured)
    model     TEXT,
    -- Model filename, e.g. "ggml-small.bin" (NULL if not recorded)
    language  TEXT
    -- Language code used, e.g. "en", "auto" (NULL if not recorded)
);

CREATE INDEX IF NOT EXISTS idx_transcriptions_timestamp
    ON transcriptions (timestamp DESC);
```

GRDB Swift model:

```swift
struct TranscriptionRecord: Codable, FetchableRecord, PersistableRecord {
    var id: Int64?
    var text: String
    var timestamp: String        // ISO 8601
    var duration: Double?
    var model: String?
    var language: String?

    static let databaseTableName = "transcriptions"
}
```

---

### Appendix C — Whisper Model Download URLs

| Model | Filename | URL | Approx Size |
|-------|----------|-----|-------------|
| tiny | ggml-tiny.bin | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin | 75MB |
| base | ggml-base.bin | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin | 150MB |
| small | ggml-small.bin | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin | 500MB |
| medium | ggml-small.bin | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin | 1.5GB |
| large | ggml-large.bin | https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large.bin | 3GB |

All URLs may redirect. Use `URLSession` with redirect following enabled (default behavior). The `ggerganov/whisper.cpp` HuggingFace repository is the canonical source maintained by the whisper.cpp author.

Pre-bundled model path in app bundle: `[bundle]/Contents/Resources/models/whisper/ggml-small.bin`

---

### Appendix D — Hotkey Virtual Keycodes

The CGEvent trigger key is configured by the user as a single character. The character must be mapped to a CGKeyCode (Carbon virtual key code):

| Character | CGKeyCode | | Character | CGKeyCode |
|-----------|----------|-|-----------|----------|
| A | 0 | | N | 45 |
| B | 11 | | O | 31 |
| C | 8 | | P | 35 |
| D | 2 | | Q | 12 |
| E | 14 | | R | 15 |
| F | 3 | | S | 1 |
| G | 5 | | T | 17 |
| H | 4 | | U | 32 |
| I | 34 | | V | 9 |
| J | 38 | | W | 13 |
| K | 40 | | X | 7 |
| L | 37 | | Y | 16 |
| M | 46 | | Z | 6 |

Number keys (top row, no numpad):

| Character | CGKeyCode | | Character | CGKeyCode |
|-----------|----------|-|-----------|----------|
| 0 | 29 | | 5 | 23 |
| 1 | 18 | | 6 | 22 |
| 2 | 19 | | 7 | 26 |
| 3 | 20 | | 8 | 28 |
| 4 | 21 | | 9 | 25 |

Note: These are US keyboard layout keycodes. International keyboards may map differently. For v1, we accept this limitation. Future version: use `UCKeyTranslate` to map characters to keycodes regardless of keyboard layout.

The modifier flags required for the hotkey:
- Cmd: `CGEventFlags.maskCommand` (raw value: 0x00100000)
- Shift: `CGEventFlags.maskShift` (raw value: 0x00020000)

The tap callback checks: `event.flags.contains(.maskCommand) && event.flags.contains(.maskShift) && event.getIntegerValueField(.keyboardEventKeycode) == triggerKeyCode`

---

### Appendix E — Llama Prompt Template (Exact Text)

The following is the exact string passed as the prompt to llama.cpp. This matches the Electron v1.1.0 implementation in `whisperService.ts`:

```
Convert any file paths in this text to proper format. Output only the result, nothing else.

Transcription:
[INSERT RAW WHISPER TEXT HERE]

Result:
```

Where `[INSERT RAW WHISPER TEXT HERE]` is replaced with the literal Whisper output string.

The model generates continuation text after "Result:" and that continuation is the formatted output. The continuation is trimmed of leading/trailing whitespace before use.

llama.cpp parameters used:
- `-n 256` → max new tokens: 256
- `--temp 0.1` → temperature: 0.1 (near-deterministic)
- `--repeat-penalty 1.1` → repeat penalty (implied; should be set explicitly)

---

### Appendix F — IPC Surface Mapping (Electron → Swift)

For reference during implementation, this table maps every IPC channel in the Electron version to its Swift equivalent:

| Electron IPC Channel | Swift Equivalent |
|---------------------|-----------------|
| register-hotkey | CGEventTap setup, stored in HotkeyManager |
| unregister-hotkey | CGEventTap disable/destroy |
| get-hotkey | UserDefaults or settings.json read |
| hotkey-down (event to renderer) | NotificationCenter post, observed by RecordingCoordinator |
| hotkey-up (event to renderer) | NotificationCenter post, observed by RecordingCoordinator |
| hotkey-changed (event to renderer) | NotificationCenter post, observed by MainWindowViewModel |
| open-settings-window | MainWindowController.showSettings() |
| show-recording-overlay | RecordingOverlayController.show() |
| hide-recording-overlay | RecordingOverlayController.hide() |
| audio-level (send from renderer) | AudioEngine publishes via @Published level property |
| copy-to-clipboard | NSPasteboard.general.setString(_:forType:) |
| set-previous-window-focused | RecordingCoordinator.previousApp = NSWorkspace.shared.frontmostApplication |
| paste-to-previous-window | CGEvent Cmd+V synthesis |
| whisper:get-settings | SettingsStore.whisperSettings |
| whisper:save-settings | SettingsStore.save(whisper:) |
| whisper:get-models | FileManager scan of models/whisper/ |
| whisper:get-model-sizes | Hardcoded array in ModelManager |
| whisper:download-model | ModelManager.downloadWhisperModel(size:) |
| whisper:select-model | NSOpenPanel → SettingsStore.whisperModelPath |
| whisper:check-model | ModelManager.whisperModelStatus |
| whisper:transcribe | WhisperEngine.transcribe(wavPath:) |
| whisper:save-audio | (not needed — audio is written directly as WAV) |
| llama:get-settings | SettingsStore.llamaSettings |
| llama:save-settings | SettingsStore.save(llama:) |
| llama:check-binary | Always returns true (compiled in) |
| llama:select-model | NSOpenPanel → SettingsStore.llamaModelPath |
| llama:process-text | LlamaEngine.format(text:) |
| llama:get-models | Hardcoded availableLlamaModels array |
| llama:download-model | ModelManager.downloadLlamaModel(id:) |
| llama:download-custom-model | ModelManager.downloadHuggingFaceModel(id:) |
| llama:download-progress (event) | @Published downloadProgress in ModelManager |
| llama:get-models-path | SettingsStore.llamaModelsPath |
| transcription:log | TranscriptionStore.log(text:duration:model:language:) |
| transcription:get-all | TranscriptionStore.getAll(limit:) |
| transcription:get | TranscriptionStore.get(id:) |
| transcription:delete | TranscriptionStore.delete(id:) |
| transcription:clear | TranscriptionStore.clear() |
| open-external | NSWorkspace.shared.open(URL) |

---

*End of Document*

**whisperMeOff for Mac — PRD v1.0**
*"Speak it. Ship it. No cloud required."*
