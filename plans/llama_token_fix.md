# Plan: Fix Empty Token Logging in LlamaService

## Problem Analysis

The debug output shows many blank "[LLAMA] Got token:" lines because:

1. **Token streaming** - The Llama model generates text token-by-token via `await foreach` loop at [`LlamaService.cs:108-116`](Services/LlamaService.cs:108)
2. **Empty tokens** - Some tokens are whitespace, control characters, or special tokens that produce no visible text
3. **Result** - The model accumulates 211 raw chars but after cleanup gets 0 chars, then falls back to raw text

## Solution Plan

### Step 1: Filter Empty Tokens
Modify the token streaming loop to skip empty or whitespace-only tokens:

```csharp
await foreach (var text in _executor!.InferAsync(prompt, inferenceParams))
{
    if (!string.IsNullOrWhiteSpace(text))
    {
        System.Diagnostics.Debug.WriteLine($"[LLAMA] Got token: {text}");
        response.Append(text);
    }
    
    // Stop if we hit a reasonable length
    if (response.Length > rawText.Length * 3)
        break;
}
```

### Step 2: Improve Debug Logging
Add better logging to show actual token count vs empty tokens.

### Step 3: Review Cleanup Logic
The [`CleanupLlamaOutput`](Services/LlamaService.cs:157) method may be too aggressive - review and adjust if needed.

## Files to Modify

- [`Services/LlamaService.cs`](Services/LlamaService.cs) - Lines 108-116 for token filtering

## Implementation

Switch to Code mode to implement the fix.
