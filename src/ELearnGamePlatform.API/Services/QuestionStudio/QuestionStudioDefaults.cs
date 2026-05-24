using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ELearnGamePlatform.API.Services.QuestionStudio;
internal static class QuestionStudioDefaults
{
    public static readonly IReadOnlyCollection<string> DefaultQuestionTypes = new[] { "MultipleChoice", "Flashcard", "ShortAnswer" };
    public static readonly IReadOnlyCollection<string> DefaultDifficulties = new[] { "Easy", "Medium", "Hard" };

    public static QuestionStudioProfile ResolveProfile(string mode)
        => mode.ToLowerInvariant() switch
        {
            "fast" => new QuestionStudioProfile(1, 2, 0, 0.70, false),
            "quality" => new QuestionStudioProfile(2, 2, 2, 0.88, false),
            "max_draft" => new QuestionStudioProfile(3, 6, 1, 0.72, true),
            _ => new QuestionStudioProfile(2, 3, 1, 0.80, false)
        };

    public static bool IsValidMode(string mode)
        => mode is "fast" or "balanced" or "quality" or "max_draft";

    public static bool IsSupportedGenerationType(string type)
        => type is "MultipleChoice" or "ShortAnswer" or "TrueFalse" or "FillInTheBlank" or "Flashcard";

    public static QuestionType ParseQuestionType(string value)
        => value switch
        {
            "TrueFalse" => QuestionType.TrueFalse,
            "ShortAnswer" => QuestionType.ShortAnswer,
            "FillBlank" => QuestionType.FillInTheBlank,
            "FillInTheBlank" => QuestionType.FillInTheBlank,
            "Flashcard" => QuestionType.ShortAnswer,
            _ => QuestionType.MultipleChoice
        };

    public static DifficultyLevel ParseDifficulty(string value)
        => value switch
        {
            "Easy" => DifficultyLevel.Easy,
            "Hard" => DifficultyLevel.Hard,
            _ => DifficultyLevel.Medium
        };
}

internal sealed record QuestionStudioProfile(int CanonicalPerUnit, int VariantsPerCanonical, int MaxRepairRounds, double TargetVerifierScore, bool AllowBorderlineDrafts);

internal static class QuestionStudioDraftFactory
{
    public static QuestionDraft Create(
        QuestionGenerationRun run,
        QuestionSourceUnit? unit,
        QuestionStudioAiQuestion item,
        string draftKind,
        int? parentDraftId)
    {
        var questionType = NormalizeQuestionType(item.QuestionType);
        return new QuestionDraft
        {
            DocumentId = run.DocumentId,
            GenerationRunId = run.Id,
            SourceUnitId = unit?.Id,
            SourceUnit = unit,
            Status = "Draft",
            DraftKind = draftKind,
            ParentDraftId = parentDraftId,
            QuestionText = NormalizeText(item.QuestionText),
            QuestionType = questionType,
            OptionsJson = JsonSerializer.Serialize(NormalizeOptions(item.Options, item.CorrectAnswer, questionType)),
            CorrectAnswer = NormalizeAnswer(item.CorrectAnswer, questionType),
            Explanation = NormalizeText(item.Explanation),
            Difficulty = NormalizeDifficulty(item.Difficulty),
            LearningObjective = NormalizeLearningObjective(item.LearningObjective),
            TopicTag = unit?.TopicTag ?? string.Empty,
            SourceEvidence = NormalizeText(string.IsNullOrWhiteSpace(item.SourceEvidence) ? unit?.Content ?? string.Empty : item.SourceEvidence),
            StemHash = QuestionStudioText.HashStem(item.QuestionText ?? string.Empty),
            MetadataJson = JsonSerializer.Serialize(new { generatedBy = "question-studio-v2" })
        };
    }

    public static bool IsUsable(QuestionStudioAiQuestion item)
        => !string.IsNullOrWhiteSpace(item.QuestionText) && !string.IsNullOrWhiteSpace(item.Explanation);

    public static QuestionStudioAiQuestion BuildDeterministicQuestion(string type, string difficulty, string source, string topicTag)
    {
        var evidence = NormalizeText(source);
        var focus = Truncate(evidence, 180);
        type = NormalizeQuestionType(type);

        if (type == "Flashcard")
        {
            return new QuestionStudioAiQuestion
            {
                QuestionText = $"What should learners remember about {topicTag}?",
                QuestionType = "Flashcard",
                CorrectAnswer = focus,
                Explanation = $"This answer is grounded in the source evidence: {Truncate(evidence, 220)}",
                Difficulty = difficulty,
                LearningObjective = "Remember",
                SourceEvidence = evidence
            };
        }

        if (type == "ShortAnswer")
        {
            return new QuestionStudioAiQuestion
            {
                QuestionText = $"Summarize the key idea about {topicTag}.",
                QuestionType = "ShortAnswer",
                CorrectAnswer = focus,
                Explanation = $"The source states this idea directly: {Truncate(evidence, 220)}",
                Difficulty = difficulty,
                LearningObjective = "Understand",
                SourceEvidence = evidence
            };
        }

        if (type == "TrueFalse")
        {
            return new QuestionStudioAiQuestion
            {
                QuestionText = $"True or false: {focus}",
                QuestionType = "TrueFalse",
                Options = new List<string> { "A. True", "B. False" },
                CorrectAnswer = "A",
                Explanation = $"The statement is taken from the source evidence: {Truncate(evidence, 220)}",
                Difficulty = difficulty,
                LearningObjective = "Remember",
                SourceEvidence = evidence
            };
        }

        return new QuestionStudioAiQuestion
        {
            QuestionText = $"Which statement best matches the source about {topicTag}?",
            QuestionType = "MultipleChoice",
            Options = new List<string>
            {
                $"A. {focus}",
                "B. The source says the opposite.",
                "C. The source does not mention this topic.",
                "D. The topic is unrelated to the document."
            },
            CorrectAnswer = "A",
            Explanation = $"Option A is grounded in the source evidence: {Truncate(evidence, 220)}",
            Difficulty = difficulty,
            LearningObjective = "Understand",
            SourceEvidence = evidence
        };
    }

    public static QuestionStudioAiQuestion BuildVariantQuestion(string type, string difficulty, QuestionDraft canonical)
    {
        type = NormalizeQuestionType(type);
        if (type == canonical.QuestionType)
        {
            type = canonical.QuestionType == "MultipleChoice" ? "ShortAnswer" : "MultipleChoice";
        }

        var evidence = string.IsNullOrWhiteSpace(canonical.SourceEvidence) ? canonical.Explanation : canonical.SourceEvidence;
        var baseAnswer = string.IsNullOrWhiteSpace(canonical.CorrectAnswer) ? Truncate(evidence, 160) : canonical.CorrectAnswer;

        if (type == "ShortAnswer" || type == "Flashcard")
        {
            return new QuestionStudioAiQuestion
            {
                QuestionText = $"Explain the idea tested by: {canonical.QuestionText}",
                QuestionType = type,
                CorrectAnswer = baseAnswer,
                Explanation = $"This variant keeps the same source-grounded answer. {canonical.Explanation}",
                Difficulty = difficulty,
                LearningObjective = "Understand",
                SourceEvidence = evidence
            };
        }

        return new QuestionStudioAiQuestion
        {
            QuestionText = $"Which answer is most consistent with this source evidence: {Truncate(evidence, 120)}?",
            QuestionType = "MultipleChoice",
            Options = new List<string>
            {
                $"A. {baseAnswer}",
                "B. A statement not supported by the source.",
                "C. A detail from another topic.",
                "D. There is not enough information."
            },
            CorrectAnswer = "A",
            Explanation = $"Option A preserves the canonical answer. {canonical.Explanation}",
            Difficulty = difficulty,
            LearningObjective = "Apply",
            SourceEvidence = evidence
        };
    }

    public static List<QuestionOption> ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<QuestionOption>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<QuestionOption>>(json) ?? new List<QuestionOption>();
        }
        catch
        {
            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return NormalizeOptions(values, "A", "MultipleChoice");
            }
            catch
            {
                return new List<QuestionOption>();
            }
        }
    }

    private static List<QuestionOption> NormalizeOptions(List<string>? rawOptions, string? correctAnswer, string questionType)
    {
        if (questionType is "ShortAnswer" or "Flashcard" or "FillInTheBlank")
        {
            return new List<QuestionOption>();
        }

        if (questionType == "TrueFalse")
        {
            var normalizedAnswer = NormalizeAnswer(correctAnswer, questionType);
            return new List<QuestionOption>
            {
                new() { Key = "A", Text = "True", IsCorrect = normalizedAnswer == "A" },
                new() { Key = "B", Text = "False", IsCorrect = normalizedAnswer == "B" }
            };
        }

        var keys = new[] { "A", "B", "C", "D", "E", "F" };
        var normalized = (rawOptions ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select((value, index) => ParseOption(value, keys.ElementAtOrDefault(index) ?? "A"))
            .GroupBy(x => x.Text.Trim().ToLowerInvariant())
            .Select(x => x.First())
            .Take(6)
            .ToList();

        while (normalized.Count < 4)
        {
            var key = keys[normalized.Count];
            normalized.Add(new QuestionOption { Key = key, Text = $"Reference option {key}", IsCorrect = false });
        }

        var answer = NormalizeAnswer(correctAnswer, questionType);
        foreach (var option in normalized)
        {
            option.IsCorrect = string.Equals(option.Key, answer, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option.Text, correctAnswer, StringComparison.OrdinalIgnoreCase);
        }

        if (!normalized.Any(x => x.IsCorrect))
        {
            normalized[0].IsCorrect = true;
        }

        return normalized;
    }

    private static QuestionOption ParseOption(string value, string fallbackKey)
    {
        var match = Regex.Match(value.Trim(), @"^([A-Fa-f])[\).\:\-]\s*(.+)$");
        if (match.Success)
        {
            return new QuestionOption
            {
                Key = match.Groups[1].Value.ToUpperInvariant(),
                Text = Truncate(NormalizeText(match.Groups[2].Value), 260),
                IsCorrect = false
            };
        }

        return new QuestionOption
        {
            Key = fallbackKey,
            Text = Truncate(NormalizeText(value), 260),
            IsCorrect = false
        };
    }

    private static string NormalizeQuestionType(string? value)
        => value switch
        {
            "TrueFalse" => "TrueFalse",
            "ShortAnswer" => "ShortAnswer",
            "FillBlank" => "FillInTheBlank",
            "FillInTheBlank" => "FillInTheBlank",
            "MatchPair" => "ShortAnswer",
            "Flashcard" => "Flashcard",
            _ => "MultipleChoice"
        };

    private static string NormalizeAnswer(string? value, string questionType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return questionType is "ShortAnswer" or "Flashcard" or "FillInTheBlank"
                ? string.Empty
                : "A";
        }

        if (questionType == "TrueFalse")
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "false" => "B",
                "b" => "B",
                _ => "A"
            };
        }

        return Truncate(NormalizeText(value), 500);
    }

    private static string NormalizeDifficulty(string? value)
        => value switch
        {
            "Easy" => "Easy",
            "Hard" => "Hard",
            _ => "Medium"
        };

    private static string NormalizeLearningObjective(string? value)
        => value switch
        {
            "Remember" => "Remember",
            "Apply" => "Apply",
            "Analyze" => "Analyze",
            _ => "Understand"
        };

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Replace('\u00A0', ' '), @"\s+", " ").Trim();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}

internal static class QuestionStudioText
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "about", "what", "which",
        "mot", "cac", "cho", "voi", "trong", "duoc", "khong", "nhung", "theo"
    };

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string HashStem(string value)
        => Hash(NormalizeStem(value));

    public static HashSet<string> Tokenize(string value)
        => NormalizeStem(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3 && !StopWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeStem(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }
}

internal sealed class QuestionStudioAiQuestionList
{
    public List<QuestionStudioAiQuestion>? Questions { get; set; }
}

internal sealed class QuestionStudioAiQuestion
{
    public string? QuestionText { get; set; }
    public string? QuestionType { get; set; }
    public List<string>? Options { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Explanation { get; set; }
    public string? Difficulty { get; set; }
    public string? LearningObjective { get; set; }
    public string? SourceEvidence { get; set; }
}

internal sealed class QuestionStudioVerificationResult
{
    public double GroundingScore { get; set; }
    public double AnswerScore { get; set; }
    public double ClarityScore { get; set; }
    public string? FailureReason { get; set; }
}

