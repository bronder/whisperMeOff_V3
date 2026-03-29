# Text Transformation Expansion Ideas

## Current Transformations
- **Tone** (Formal ↔ Informal)
- **Voice** (Active ↔ Passive)
- **Complexity** (Simplify ↔ Elaborate)
- **Professionalism** (Professional ↔ Casual)
- **Grammar** (Correction)
- **Translation** (Multiple languages)
- **PersonalStyle** (User-defined preferences)
- **Custom** (User-defined transformations)

---

## New Transformation Ideas

### Priority 1: High Value, Low Effort

| Transformation | Description | Directions |
|----------------|-------------|------------|
| **Reading Level** | Adjust text to specific grade levels | Elementary, Middle School, High School, College, Graduate |
| **Audience** | Target text to specific audiences | Child, Teen, Expert, Layperson |
| **Perspective** | Change narrative perspective | First Person, Second Person, Third Person |
| **Format** | Convert between formats | Bullet Points, Paragraph, Email, Memo, SMS |

### Priority 2: High Value, Medium Effort

| Transformation | Description | Directions |
|----------------|-------------|------------|
| **Sentiment** | Adjust emotional tone | Positive, Negative, Neutral, Empathetic, Urgent |
| **Regional** | Convert between English dialects | American, British, Australian, Canadian |
| **Confidence** | Adjust certainty level | Authoritative, Hedged |
| **Politeness** | Communication style | Assertive, Diplomatic, Polite |

### Priority 3: Medium Value, Higher Effort

| Transformation | Description | Directions |
|----------------|-------------|------------|
| **Inclusive** | Inclusive language options | Gender-Neutral, Culturally-Sensitive, Disability-Inclusive |
| **Marketing** | Sales/promotional copy | Persuasive, CTA, Feature-List |
| **Legal** | Business/legal language | Contract-Ready, Policy-Speak, Compliance |
| **Time Style** | Temporal language style | Modern, Vintage/Classical |
| **Creative** | Writing style variations | Narrative, Expository, Persuasive |
| **Humor** | Humor tone | Serious, Humorous, Sarcastic, Warm/Friendly |

---

## Recommended Implementation Order

1. **Reading Level** - Very useful, straightforward implementation
2. **Audience** - Complements existing complexity transformation
3. **Format** - Practical for document conversion
4. **Perspective** - Useful for rewriting tasks
5. **Sentiment** - Popular feature for content creators
6. **Regional** - Good for international communication
7. **Confidence/Politeness** - Refine tone further
8. **Inclusive Language** - Important for accessibility
9. **Marketing/Legal** - Specialized business use cases
10. **Time Style/Creative/Humor** - Creative enhancements

---

## Implementation Overview

Each new transformation type requires:
1. Add `TransformationType` enum value
2. Add `TransformationDirection` enum values (or create direction enum per type)
3. Create prompt template in `TransformationPrompts.cs`
4. Add direction handling in `GetTransformationPrompt()`
5. Add UI elements in `MainWindow.xaml` if user-facing
6. Add tests for transformation quality

---

## Next Steps

Choose which transformation types to implement, and I will create a detailed implementation plan with:
- File changes required
- Code modifications needed
- UI updates necessary
- Testing approach
