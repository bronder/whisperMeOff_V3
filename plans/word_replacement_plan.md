# Word Replacement Feature

## Overview
Allow users to define find/replace pairs so that when certain phrases are transcribed, they are automatically replaced with different text. This is useful for:
- Fixing misrecognized words
- Expanding abbreviations
- Creating shortcuts (e.g., "yaba daba do" → "yum")

## Data Model

### Settings Storage (JSON)
```json
{
  "WordReplacements": [
    { "Source": "yaba daba do", "Replacement": "yum" },
    { "Source": "API", "Replacement": "A P I" },
    { "Source": "todo", "Replacement": "to-do" }
  ]
}
```

### C# Classes
```csharp
public class WordReplacement
{
    public string Source { get; set; } = "";
    public string Replacement { get; set; } = "";
}

public class WordReplacementSettings
{
    public List<WordReplacement> Replacements { get; set; } = new();
}
```

## UI Design

### Vocabulary Tab Layout
```
┌─────────────────────────────────────────────┐
│ Custom Vocabulary                          │
│ [TextBox for vocabulary words]              │
│                                            │
├─────────────────────────────────────────────┤
│ Word Replacements                          │
│ ┌──────────────┬──────────────┬──────────┐  │
│ │ Source      │ Replacement  │ Actions  │  │
│ ├──────────────┼──────────────┼──────────┤  │
│ │ yaba daba.. │ yum          │ [x]     │  │
│ │ API         │ A P I        │ [x]     │  │
│ └──────────────┴──────────────┴──────────┘  │
│ [+ Add New]                                │
└─────────────────────────────────────────────┘
```

## Implementation Steps

1. Add `WordReplacementSettings` to SettingsService.cs
2. Add UI components to Vocabulary tab (DataGrid or ListView with Add/Remove buttons)
3. Add code-behind handlers for Add/Remove functionality
4. Add replacement logic in WhisperService.TranscribeAsync() after transcription
5. Update SettingsService.Load/Save to handle word replacements
