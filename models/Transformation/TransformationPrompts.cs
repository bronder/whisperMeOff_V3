using System.Collections.Generic;
using whisperMeOff.Models.Transformation;

namespace whisperMeOff.Models.Transformation;

/// <summary>
/// Provides prompt templates for different transformation types.
/// These prompts are used with the Llama model to perform text transformations.
/// </summary>
public static class TransformationPrompts
{
    /// <summary>
    /// System prompt that defines the transformation assistant's role and constraints.
    /// </summary>
    public const string SystemPrompt = @"You are a text transformation assistant. Your task is to transform text according to the specified transformation type while:
1. Preserving the original meaning as much as possible
2. Maintaining grammatical correctness
3. Preserving proper nouns (names, places, organizations) unless instructed otherwise
4. Preserving technical terminology unless instructed otherwise
5. Keeping the output natural and fluent

Respond ONLY with the transformed text. Do not include any explanations, preambles, or additional text.";

    /// <summary>
    /// Gets the transformation prompt for a specific transformation type and direction.
    /// </summary>
    public static string GetTransformationPrompt(TransformationType type, TransformationDirection direction)
    {
        return type switch
        {
            TransformationType.Tone => GetTonePrompt(direction),
            TransformationType.Voice => GetVoicePrompt(direction),
            TransformationType.Complexity => GetComplexityPrompt(direction),
            TransformationType.Professionalism => GetProfessionalismPrompt(direction),
            TransformationType.Grammar => GetGrammarPrompt(),
            TransformationType.Translation => GetTranslationPrompt(),
            TransformationType.PersonalStyle => GetPersonalStylePrompt(),
            TransformationType.Custom => GetCustomPrompt(),
            _ => GetDefaultPrompt()
        };
    }

    private static string GetCustomPrompt()
    {
        return @"Transform the following text according to the specified custom requirements.
- Follow the custom transformation parameters provided
- Preserve the core message and meaning
- Maintain grammatical correctness
- Keep any specified terms unchanged

Text to transform:";
    }

    private static string GetTonePrompt(TransformationDirection direction)
    {
        return direction switch
        {
            TransformationDirection.Formal => @"Transform the following text to a more FORMAL tone. 
- Use formal vocabulary and phrases
- Avoid contractions and colloquialisms
- Use proper grammar and sentence structure
- Maintain professional language

Text to transform:",
            TransformationDirection.Informal => @"Transform the following text to a more INFORMAL/CASUAL tone.
- Use contractions where appropriate
- Include casual expressions naturally
- Keep it friendly but readable
- Avoid overly stiff language

Text to transform:",
            _ => @"Transform the following text according to the specified tone direction. Maintain the original meaning while adjusting the formality level.

Text to transform:"
        };
    }

    private static string GetVoicePrompt(TransformationDirection direction)
    {
        return direction switch
        {
            TransformationDirection.Active => @"Convert the following text from PASSIVE voice to ACTIVE voice.
- Identify the subject performing the action
- Put the subject in the subject position
- Make the agent of the action explicit
- Keep the same meaning

Text to transform:",
            TransformationDirection.Passive => @"Convert the following text from ACTIVE voice to PASSIVE voice.
- Move the object to the subject position
- Use appropriate passive voice constructions
- Optionally include the agent with 'by'
- Keep the same meaning

Text to transform:",
            _ => @"Transform the following text, changing the voice between active and passive as appropriate while preserving meaning.

Text to transform:"
        };
    }

    private static string GetComplexityPrompt(TransformationDirection direction)
    {
        return direction switch
        {
            TransformationDirection.Simplify => @"Simplify the following text to make it EASIER TO UNDERSTAND.
- Use simpler, more common words
- Shorten long sentences where possible
- Remove unnecessary jargon
- Keep the core meaning intact
- Aim for a reading level appropriate for general audiences

Text to transform:",
            TransformationDirection.Elaborate => @"Elaborate on the following text to provide MORE DETAIL and DEPTH.
- Add relevant descriptive information
- Expand abbreviations and acronyms
- Provide more context where helpful
- Maintain the original meaning
- Keep it natural and not overly verbose

Text to transform:",
            _ => @"Transform the following text, adjusting its complexity level while preserving the core meaning.

Text to transform:"
        };
    }

    private static string GetProfessionalismPrompt(TransformationDirection direction)
    {
        return direction switch
        {
            TransformationDirection.Professional => @"Transform the following text to a more PROFESSIONAL tone suitable for business contexts.
- Use business-appropriate vocabulary
- Be clear and concise
- Maintain a respectful, formal tone
- Avoid slang and casual expressions
- Structure sentences professionally

Text to transform:",
            TransformationDirection.Casual => @"Transform the following text to a more CASUAL, FRIENDLY tone.
- Use conversational language
- Be warm and approachable
- Keep it natural and relaxed
- Avoid overly formal language
- Maintain clarity

Text to transform:",
            _ => @"Transform the following text, adjusting between professional and casual tone while maintaining the message.

Text to transform:"
        };
    }

    private static string GetGrammarPrompt()
    {
        return @"Fix any grammatical errors, punctuation issues, and spelling mistakes in the following text.
- Correct verb tenses and subject-verb agreement
- Fix punctuation errors
- Correct spelling mistakes
- Ensure proper sentence structure
- Preserve the original meaning and style as much as possible
- Only change what is incorrect, don't rewrite unnecessarily

Text to transform:";
    }

    private static string GetTranslationPrompt()
    {
        return @"Translate the following text to the target language.
- Maintain the original meaning as accurately as possible
- Preserve the tone and style of the original
- Use natural phrasing in the target language
- Keep proper nouns in their original form
- Do not add explanations or notes

Text to translate:";
    }

    private static string GetPersonalStylePrompt()
    {
        return @"Apply the following personal style preferences to transform the text:
- Follow any specified style guidelines
- Incorporate preferred vocabulary or phrases
- Match the desired tone and voice
- Preserve the core message
- Make it sound natural to the specified style

Text to transform:";
    }

    private static string GetDefaultPrompt()
    {
        return @"Transform the following text according to the specified requirements while:
- Preserving the original meaning
- Maintaining grammatical correctness
- Keeping proper nouns unchanged
- Preserving technical terminology

Text to transform:";
    }

    /// <summary>
    /// Gets additional instructions for preserving specific terms.
    /// </summary>
    public static string GetPreservationInstructions(bool preserveProperNouns, bool preserveTechnicalTerms, List<string>? termsToPreserve = null)
    {
        var instructions = new List<string>();

        if (preserveProperNouns)
        {
            instructions.Add("Do NOT change any proper nouns (names, places, organizations, brands).");
        }

        if (preserveTechnicalTerms)
        {
            instructions.Add("Do NOT change any technical terminology or specialized vocabulary.");
        }

        if (termsToPreserve != null && termsToPreserve.Count > 0)
        {
            instructions.Add($"The following terms must be preserved exactly: {string.Join(", ", termsToPreserve)}");
        }

        return instructions.Count > 0 
            ? "\n\nIMPORTANT CONSTRAINTS:\n" + string.Join("\n", instructions)
            : string.Empty;
    }

    /// <summary>
    /// Builds a complete prompt for transformation.
    /// </summary>
    public static string BuildPrompt(TransformationRequest request, List<string>? termsToPreserve = null)
    {
        var transformationPrompt = GetTransformationPrompt(request.TransformationType, request.Direction);
        
        var preservationInstructions = GetPreservationInstructions(
            request.PreserveProperNouns, 
            request.PreserveTechnicalTerms,
            termsToPreserve);

        return transformationPrompt + "\n\n" + request.Text + preservationInstructions;
    }

    /// <summary>
    /// Builds a prompt for translation with target language.
    /// </summary>
    public static string BuildTranslationPrompt(string text, string targetLanguage, bool preserveProperNouns = true, bool preserveTechnicalTerms = true)
    {
        var languageNames = new Dictionary<string, string>
        {
            { "en", "English" },
            { "es", "Spanish" },
            { "fr", "French" },
            { "de", "German" },
            { "it", "Italian" },
            { "pt", "Portuguese" },
            { "ru", "Russian" },
            { "ja", "Japanese" },
            { "ko", "Korean" },
            { "zh", "Chinese" },
            { "ar", "Arabic" },
            { "hi", "Hindi" }
        };

        var targetLanguageName = languageNames.TryGetValue(targetLanguage.ToLowerInvariant(), out var langName)
            ? langName
            : targetLanguage;

        var prompt = $"Translate the following text to {targetLanguageName}.\n";
        prompt += "- Maintain the original meaning as accurately as possible\n";
        prompt += "- Preserve the tone and style of the original\n";
        prompt += "- Use natural phrasing in the target language\n";

        if (preserveProperNouns)
        {
            prompt += "- Keep proper nouns (names, places, organizations) in their original form\n";
        }

        if (preserveTechnicalTerms)
        {
            prompt += "- Keep technical terminology in their original form\n";
        }

        prompt += "- Do not add explanations or notes\n\n";
        prompt += "Text to translate:\n";
        prompt += text;

        return prompt;
    }

    /// <summary>
    /// Builds a prompt for applying a transformation profile.
    /// </summary>
    public static string BuildProfilePrompt(TransformationRequest request, TransformationProfile profile)
    {
        var prompt = GetTransformationPrompt(profile.TransformationType, profile.Direction);
        
        if (!string.IsNullOrEmpty(profile.Description))
        {
            prompt += $"\n\nProfile description: {profile.Description}";
        }

        if (profile.Parameters.Count > 0)
        {
            prompt += "\n\nAdditional parameters:\n";
            foreach (var param in profile.Parameters)
            {
                prompt += $"- {param.Key}: {param.Value}\n";
            }
        }

        var preservationInstructions = GetPreservationInstructions(
            profile.PreserveProperNouns,
            profile.PreserveTechnicalTerms);

        return prompt + "\n\n" + request.Text + preservationInstructions;
    }

    /// <summary>
    /// Prompt for quality assessment of transformed text.
    /// </summary>
    public const string QualityAssessmentPrompt = @"Analyze the following original and transformed text pairs and assess the quality of the transformation.

Original text:
{0}

Transformed text:
{1}

Provide a quality assessment considering:
1. Meaning fidelity: How well is the original meaning preserved?
2. Grammatical correctness: Is the transformed text grammatically correct?
3. Naturalness: Does the transformed text sound natural?
4. Appropriateness: Does it match the requested transformation type?

Respond in JSON format:
{
  ""similarity_score"": (0-100 score for meaning similarity),
  ""confidence_score"": (0-100 confidence in the transformation quality),
  ""readability_score"": (0-100 readability of transformed text),
  ""issues"": [""list of any issues found""],
  ""acceptable"": true/false
}";

    /// <summary>
    /// Builds the quality assessment prompt with the given texts.
    /// </summary>
    public static string BuildQualityPrompt(string originalText, string transformedText)
    {
        return string.Format(QualityAssessmentPrompt, originalText, transformedText);
    }
}
