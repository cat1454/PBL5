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
public sealed class QuestionDraftVerifier : IQuestionDraftVerifier
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<QuestionDraftVerifier> _logger;

    public QuestionDraftVerifier(IOllamaService ollamaService, ILogger<QuestionDraftVerifier> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task VerifyAsync(QuestionDraft draft, CancellationToken cancellationToken = default)
    {
        var localIssues = VerifyLocally(draft);
        if (localIssues.Count > 0)
        {
            ApplyScores(draft, 0.45, 0.45, 0.55, string.Join(" ", localIssues));
            return;
        }

        var aiResult = await VerifyWithAiAsync(draft);
        if (aiResult != null)
        {
            ApplyScores(
                draft,
                Math.Clamp(aiResult.GroundingScore, 0, 1),
                Math.Clamp(aiResult.AnswerScore, 0, 1),
                Math.Clamp(aiResult.ClarityScore, 0, 1),
                aiResult.FailureReason ?? string.Empty);
            return;
        }

        var evidenceBonus = !string.IsNullOrWhiteSpace(draft.SourceEvidence) ? 0.9 : 0.72;
        ApplyScores(draft, evidenceBonus, 0.82, 0.82, string.Empty);
    }

    private static List<string> VerifyLocally(QuestionDraft draft)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.QuestionText)) issues.Add("Question text is required.");
        if (RequiresAnswer(draft.QuestionType) && string.IsNullOrWhiteSpace(draft.CorrectAnswer)) issues.Add("Correct answer is required.");
        if (RequiresAnswer(draft.QuestionType) && IsPlaceholderAnswer(draft.QuestionType, draft.CorrectAnswer)) issues.Add("Correct answer looks like a placeholder.");
        if (draft.QuestionText.Length > 700) issues.Add("Question text is too long.");
        if (draft.QuestionText.Contains("```", StringComparison.Ordinal) || draft.QuestionText.Contains("{\"", StringComparison.Ordinal)) issues.Add("Question text contains markup or JSON artifacts.");
        if (draft.DraftKind == "Canonical" && string.IsNullOrWhiteSpace(draft.SourceEvidence)) issues.Add("Source evidence is required.");

        var options = QuestionStudioDraftFactory.ParseOptions(draft.OptionsJson);
        if (draft.QuestionType == "MultipleChoice")
        {
            if (options.Count < 4) issues.Add("MultipleChoice requires at least four options.");
            if (options.Select(x => x.Text.Trim().ToLowerInvariant()).Distinct().Count() != options.Count) issues.Add("MultipleChoice options must be unique.");
            if (!options.Any(x => string.Equals(x.Key, draft.CorrectAnswer, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Text, draft.CorrectAnswer, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add("Correct answer must match an option key or text.");
            }
        }

        if (draft.Explanation.Length < 18) issues.Add("Explanation is too short.");
        return issues;
    }

    private static bool IsPlaceholderAnswer(string questionType, string? answer)
    {
        if (questionType is not ("ShortAnswer" or "Flashcard" or "FillInTheBlank"))
        {
            return false;
        }

        var normalized = answer?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "A", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "N/A", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "TODO", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Reference option ", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<QuestionStudioVerificationResult?> VerifyWithAiAsync(QuestionDraft draft)
    {
        try
        {
            var prompt = $@"Verify this study question against source evidence only.

Question: {draft.QuestionText}
Options: {draft.OptionsJson}
Correct answer: {draft.CorrectAnswer}
Explanation: {draft.Explanation}
Source evidence: {draft.SourceEvidence}

Return JSON only. All score fields must be JSON numbers between 0.0 and 1.0.
Never return scores as strings, empty strings, null, percentages, or words.
{{
  ""groundingScore"": 0.0,
  ""answerScore"": 0.0,
  ""clarityScore"": 0.0,
  ""failureReason"": """"
}}";
            return await _ollamaService.GenerateStructuredResponseAsync<QuestionStudioVerificationResult>(
                prompt,
                "Return valid JSON only. Score fields must be numeric JSON values from 0.0 to 1.0, never strings.",
                OllamaModelProfile.Verification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI verification failed for question draft {DraftId}", draft.Id);
            return null;
        }
    }

    private static void ApplyScores(QuestionDraft draft, double groundingScore, double answerScore, double clarityScore, string failureReason)
    {
        draft.GroundingScore = groundingScore;
        draft.AnswerScore = answerScore;
        draft.ClarityScore = clarityScore;
        draft.DuplicateScore = draft.DuplicateScore <= 0 ? 1.0 : draft.DuplicateScore;
        draft.OverallScore = Math.Round((groundingScore * 0.4) + (answerScore * 0.3) + (clarityScore * 0.2) + (draft.DuplicateScore * 0.1), 4);
        draft.FailureReason = failureReason;
        draft.Status = draft.OverallScore switch
        {
            >= 0.85 => "Verified",
            >= 0.70 => "Borderline",
            >= 0.50 => "Rejected",
            _ => "Quarantined"
        };
        draft.VerifiedAt = DateTime.UtcNow;
        draft.StemHash = QuestionStudioText.HashStem(draft.QuestionText);
    }

    private static bool RequiresAnswer(string type)
        => type is "MultipleChoice" or "ShortAnswer" or "TrueFalse" or "FillInTheBlank" or "Flashcard";
}

