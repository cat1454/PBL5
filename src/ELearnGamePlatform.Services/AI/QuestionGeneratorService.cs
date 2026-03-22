using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.Services.AI;

public class QuestionGeneratorService : IQuestionGenerator
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<QuestionGeneratorService> _logger;
    private const string OutputLanguage = "Vietnamese";
    private const int ChunkSize = 6500;
    private const int ChunkOverlap = 300;
    private const int GenerationBatchSize = 6;
    private const int EvidenceChunkLimit = 3;
    private const int EvidenceExcerptLength = 750;
    private const int TotalStageCount = 7;
    private const int QuestionRetryLimit = 1;
    private const string DefaultTopicTag = "noi-dung-trong-tam:chi-tiet-quan-trong";

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "va", "la", "cua", "cho", "voi", "trong", "tren", "duoc", "mot", "nhung", "cac", "khi", "neu",
        "thi", "tai", "theo", "ve", "den", "tu", "co", "khong", "nay", "do", "day", "sau", "truoc",
        "hoac", "nhu", "da", "dang", "can", "phan", "page", "from", "with", "that", "this", "have",
        "into", "about", "their", "there", "would", "should", "could", "while", "where", "which"
    };

    public QuestionGeneratorService(IOllamaService ollamaService, ILogger<QuestionGeneratorService> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public Task<List<Question>> GenerateQuestionsAsync(
        int documentId,
        string content,
        int count = 10,
        ProcessedContent? processedContent = null,
        IProgress<QuestionGenerationProgressUpdate>? progress = null)
        => GenerateQuestionsCoreAsync(documentId, content, QuestionType.MultipleChoice, count, processedContent, progress);

    public Task<List<Question>> GenerateQuestionsByTypeAsync(
        int documentId,
        string content,
        QuestionType type,
        int count = 10,
        ProcessedContent? processedContent = null,
        IProgress<QuestionGenerationProgressUpdate>? progress = null)
        => GenerateQuestionsCoreAsync(documentId, content, type, count, processedContent, progress);

    private async Task<List<Question>> GenerateQuestionsCoreAsync(
        int documentId,
        string content,
        QuestionType type,
        int count,
        ProcessedContent? processedContent,
        IProgress<QuestionGenerationProgressUpdate>? progress)
    {
        var failed = false;
        var completionReported = false;

        try
        {
            var normalizedContent = NormalizeDocumentContent(content);
            if (string.IsNullOrWhiteSpace(normalizedContent))
            {
                ReportProgress(progress, 95, "fallback", "Tai lieu rong, chuyen sang cau hoi du phong", stageLabel: "Fallback", detail: "Khong co noi dung hop le de lap coverage map", stageIndex: 6, stageCount: TotalStageCount);
                completionReported = true;
                return CreateFallbackQuestions(documentId, count, type, plans: null, bundles: null);
            }

            var hasStoredCoverageMap = processedContent?.CoverageMap.Any() == true;
            ReportProgress(
                progress,
                5,
                "building-coverage-map",
                hasStoredCoverageMap ? "Dang tai su dung coverage map da luu" : "Dang lap coverage map cho toan tai lieu",
                stageLabel: "Coverage map",
                detail: hasStoredCoverageMap
                    ? $"Tai su dung {processedContent!.CoverageMap.Count} coverage chunk tu buoc upload"
                    : "Tach tai lieu thanh cac phan noi dung theo thu tu",
                stageIndex: 2,
                stageCount: TotalStageCount);
            var chunks = GetCoverageChunks(normalizedContent, processedContent, progress);
            if (!chunks.Any())
            {
                ReportProgress(progress, 95, "fallback", "Khong tao duoc coverage map, chuyen sang fallback", stageLabel: "Fallback", detail: "Khong tao duoc chunk hop le de sinh cau hoi", stageIndex: 6, stageCount: TotalStageCount);
                completionReported = true;
                return CreateFallbackQuestions(documentId, count, type, plans: null, bundles: null);
            }

            ReportProgress(progress, 34, "planning-questions", "Dang lap ke hoach phu tai lieu", stageLabel: "Lap ke hoach cau hoi", detail: "Dung coverage map de phan bo cau hoi theo dau, giua, cuoi tai lieu", stageIndex: 3, stageCount: TotalStageCount);
            var plans = await BuildQuestionPlansAsync(chunks, processedContent, type, count);
            if (!plans.Any())
            {
                plans = BuildFallbackPlans(chunks, count, type);
            }

            ReportProgress(progress, 52, "retrieving-evidence", "Dang lay evidence cho tung cau hoi", stageLabel: "Lay evidence", detail: "Chon cac chunk lien quan nhat cho moi ke hoach cau hoi", stageIndex: 4, stageCount: TotalStageCount);
            var bundles = BuildEvidenceBundles(chunks, plans, progress);

            ReportProgress(progress, 68, "generating-grounded-questions", "Dang sinh grounded questions", stageLabel: "Sinh grounded questions", detail: "Moi batch chi duoc dung evidence da chon", stageIndex: 5, stageCount: TotalStageCount);
            var questions = await GenerateGroundedQuestionsAsync(documentId, type, plans, bundles, progress);

            if (!questions.Any())
            {
                _logger.LogWarning("Grounded generation returned no questions. Using fallback.");
                ReportProgress(progress, 92, "fallback", "AI khong tra ve cau hoi hop le, chuyen sang fallback", stageLabel: "Fallback", detail: "Sinh bo cau hoi du phong dua tren evidence da thu thap", stageIndex: 6, stageCount: TotalStageCount);
                completionReported = true;
                return CreateFallbackQuestions(documentId, count, type, plans, bundles);
            }

            ReportProgress(progress, 94, "normalizing-output", "Dang chuan hoa ket qua", stageLabel: "Chuan hoa ket qua", detail: $"Da tao {questions.Count} cau hoi, dang chuan hoa topic va explanation", current: questions.Count, total: questions.Count, unitLabel: "cau hoi", stageIndex: 6, stageCount: TotalStageCount);
            completionReported = true;
            ReportProgress(progress, 95, "completed", "Hoan tat sinh cau hoi", stageLabel: "Hoan tat sinh cau hoi", detail: $"Da tao {questions.Count} cau hoi grounded tu evidence", current: questions.Count, total: questions.Count, unitLabel: "cau hoi", stageIndex: 6, stageCount: TotalStageCount);
            return questions;
        }
        catch (Exception ex)
        {
            failed = true;
            _logger.LogError(ex, "Error generating grounded {Type} questions", type);
            ReportProgress(progress, 100, "failed", $"Loi sinh cau hoi: {ex.Message}", stageLabel: "That bai", detail: ex.Message, stageIndex: TotalStageCount, stageCount: TotalStageCount);
            return CreateFallbackQuestions(documentId, count, type, plans: null, bundles: null);
        }
        finally
        {
            if (!failed && !completionReported)
            {
                ReportProgress(progress, 95, "completed", "Hoan tat sinh cau hoi", stageLabel: "Hoan tat sinh cau hoi", detail: "Cho buoc luu ket qua vao he thong", stageIndex: 6, stageCount: TotalStageCount);
            }
        }
    }

    private List<DocumentChunk> GetCoverageChunks(
        string content,
        ProcessedContent? processedContent,
        IProgress<QuestionGenerationProgressUpdate>? progress)
    {
        if (processedContent?.CoverageMap.Any() == true)
        {
            var storedChunks = processedContent.CoverageMap
                .OrderBy(chunk => chunk.ChunkNumber)
                .Select(chunk => new DocumentChunk
                {
                    ChunkNumber = chunk.ChunkNumber,
                    ChunkId = chunk.ChunkId,
                    Zone = chunk.Zone,
                    Label = chunk.Label,
                    Summary = chunk.Summary,
                    KeyFacts = chunk.KeyFacts,
                    EvidenceExcerpt = chunk.EvidenceExcerpt,
                    SearchTokens = DocumentCoverageMapBuilder.BuildSearchTokens(chunk)
                })
                .ToList();

            ReportProgress(
                progress,
                28,
                "building-coverage-map",
                "Da nap xong coverage map da luu",
                current: storedChunks.Count,
                total: storedChunks.Count,
                stageLabel: "Coverage map",
                detail: $"Khong can tach chunk lai, dung {storedChunks.Count} coverage chunk co san",
                unitLabel: "chunk",
                stageIndex: 2,
                stageCount: TotalStageCount);

            return storedChunks;
        }

        return BuildCoverageChunks(content, progress);
    }

    private List<DocumentChunk> BuildCoverageChunks(string content, IProgress<QuestionGenerationProgressUpdate>? progress)
    {
        var coverageMap = DocumentCoverageMapBuilder.Build(content, ChunkSize, ChunkOverlap);
        var chunks = new List<DocumentChunk>(coverageMap.Count);

        for (var index = 0; index < coverageMap.Count; index++)
        {
            var coverageChunk = coverageMap[index];
            var chunk = new DocumentChunk
            {
                ChunkNumber = coverageChunk.ChunkNumber,
                ChunkId = coverageChunk.ChunkId,
                Zone = coverageChunk.Zone,
                Label = coverageChunk.Label,
                Summary = coverageChunk.Summary,
                KeyFacts = coverageChunk.KeyFacts,
                EvidenceExcerpt = coverageChunk.EvidenceExcerpt,
                SearchTokens = DocumentCoverageMapBuilder.BuildSearchTokens(coverageChunk)
            };

            chunks.Add(chunk);
            ReportProgress(
                progress,
                MapProgress(8, 28, index + 1, coverageMap.Count),
                "building-coverage-map",
                $"Dang lap coverage map phan {coverageChunk.ChunkNumber}/{coverageMap.Count}",
                coverageChunk.ChunkNumber,
                coverageMap.Count,
                chunk.ChunkId,
                "Coverage map",
                $"Da doc {chunk.ChunkId} ({chunk.Zone}): {chunk.Label}",
                "phan noi dung",
                2,
                TotalStageCount);
        }

        return chunks;
    }

    private async Task<List<QuestionPlan>> BuildQuestionPlansAsync(List<DocumentChunk> chunks, ProcessedContent? processedContent, QuestionType type, int count)
    {
        try
        {
            var prompt = $@"You are planning grounded {type} questions from a full educational document.

The coverage map below was built by reading every chunk of the document in order.

Precomputed document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Coverage map:
{RenderCoverageMapForPrompt(chunks)}

Requirements:
1. Return exactly {count} question briefs.
2. Spread briefs across early, middle, and late chunks.
3. Every brief must reference 1-3 valid preferredChunkIds taken exactly from the coverage map.
4. Keep each focus precise, factual, and answerable from the preferred chunks.
5. Use the precomputed analysis to prioritize true main topics and key points over incidental details.
6. topicName and subtopic must be concise Vietnamese phrases.
7. Vary difficulty across Easy, Medium, Hard when the document supports it.
8. Avoid duplicate focus, duplicate preferredChunkIds-only plans, and vague labels.
9. answerStyle should describe what the learner must recall, compare, infer, or identify.

Return JSON only:
[
  {{
    ""planId"": ""P01"",
    ""topicName"": ""ten chu de chinh"",
    ""subtopic"": ""y nho cu the"",
    ""focus"": ""noi dung kien thuc se hoi"",
    ""preferredChunkIds"": [""C01"", ""C02""],
    ""answerStyle"": ""nho khai niem / suy luan / so sanh / quy trinh"",
    ""difficulty"": ""Medium""
  }}
]";

            var drafts = await _ollamaService.GenerateStructuredResponseAsync<List<QuestionPlanDraft>>(prompt, BuildPlanningSystemPrompt(type));
            return NormalizeQuestionPlans(drafts, chunks, count, type);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error building question plans. Using deterministic fallback plans.");
            return BuildFallbackPlans(chunks, count, type);
        }
    }

    private async Task<List<Question>> GenerateGroundedQuestionsAsync(
        int documentId,
        QuestionType type,
        List<QuestionPlan> plans,
        List<EvidenceBundle> bundles,
        IProgress<QuestionGenerationProgressUpdate>? progress)
    {
        var bundleMap = bundles.ToDictionary(bundle => bundle.Plan.PlanId, StringComparer.OrdinalIgnoreCase);
        var questions = new List<Question>(plans.Count);
        var batches = plans.Chunk(GenerationBatchSize).ToList();

        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            var batchPlans = batches[batchIndex].ToList();
            var batchBundles = batchPlans
                .Select(plan => bundleMap.TryGetValue(plan.PlanId, out var bundle) ? bundle : CreateFallbackBundle(plan))
                .ToList();

            ReportProgress(
                progress,
                MapProgress(70, 90, batchIndex, batches.Count),
                "generating-grounded-questions",
                $"Dang sinh batch {batchIndex + 1}/{batches.Count}",
                batchIndex + 1,
                batches.Count,
                batchPlans.First().TopicTag,
                "Sinh grounded questions",
                $"Batch {batchIndex + 1} gom {batchPlans.Count} cau hoi voi evidence rieng",
                "batch",
                5,
                TotalStageCount);

            var generatedItems = await GenerateBatchAsync(type, batchPlans, batchBundles);
            var generatedMap = generatedItems
                .Where(item => !string.IsNullOrWhiteSpace(item.PlanId))
                .GroupBy(item => item.PlanId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < batchPlans.Count; index++)
            {
                var plan = batchPlans[index];
                var bundle = batchBundles[index];

                generatedMap.TryGetValue(plan.PlanId, out var item);
                var question = await FinalizeQuestionAsync(documentId, type, plan, bundle, item, questions);
                var usedFallback = false;

                if (question == null)
                {
                    question = CreateFallbackQuestion(documentId, type, plan, bundle, questions.Count);
                    usedFallback = true;
                }

                ApplyQuestionVerifierMetadata(question, type, bundle, questions, usedFallback);
                questions.Add(question);
            }

            ReportProgress(
                progress,
                MapProgress(70, 90, batchIndex + 1, batches.Count),
                "generating-grounded-questions",
                $"Da xong batch {batchIndex + 1}/{batches.Count}",
                batchIndex + 1,
                batches.Count,
                batchPlans.Last().TopicTag,
                "Sinh grounded questions",
                $"Da tao xong {questions.Count}/{plans.Count} cau hoi grounded",
                "batch",
                5,
                TotalStageCount);
        }

        return questions;
    }

    private async Task<List<GeneratedQuestionData>> GenerateBatchAsync(
        QuestionType type,
        List<QuestionPlan> plans,
        List<EvidenceBundle> bundles)
    {
        try
        {
            var evidenceLibrary = BuildEvidenceLibraryBlock(bundles);
            var briefs = BuildQuestionBriefBlock(plans, bundles);
            var prompt = $@"Create grounded {type} questions from the brief list and evidence library below.

Evidence library:
{evidenceLibrary}

Question briefs:
{briefs}

General hard requirements:
- Use only facts supported by the allowed evidence chunk ids of each brief.
- Do not mix evidence across plans unless the evidence ids are explicitly allowed for that plan.
- explanation must mention at least one supporting evidence chunk id in square brackets, for example [C03].
- topic must exactly equal the topicTag from the brief.
- evidenceChunkIds must be a non-empty subset of that brief's allowed evidence chunk ids.
- Return exactly {plans.Count} question objects, one for each planId.
- Write questionText, correctAnswer, and explanation in {OutputLanguage}.

{BuildTypeSpecificPrompt(type)}

Return JSON only:
{BuildTypeSpecificExample(type)}";

            var result = await _ollamaService.GenerateStructuredResponseAsync<List<GeneratedQuestionData>>(prompt, BuildGenerationSystemPrompt(type));
            return result ?? new List<GeneratedQuestionData>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating grounded question batch.");
            return new List<GeneratedQuestionData>();
        }
    }

    private List<EvidenceBundle> BuildEvidenceBundles(
        List<DocumentChunk> chunks,
        List<QuestionPlan> plans,
        IProgress<QuestionGenerationProgressUpdate>? progress)
    {
        var bundles = new List<EvidenceBundle>(plans.Count);

        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var allowedChunks = SelectEvidenceChunks(chunks, plan);
            bundles.Add(new EvidenceBundle
            {
                Plan = plan,
                EvidenceChunks = allowedChunks
            });

            ReportProgress(
                progress,
                MapProgress(54, 66, index + 1, plans.Count),
                "retrieving-evidence",
                $"Dang lay evidence cho {plan.PlanId}",
                index + 1,
                plans.Count,
                plan.TopicTag,
                "Lay evidence",
                $"Chon {allowedChunks.Count} chunk cho {plan.PlanId}: {string.Join(", ", allowedChunks.Select(chunk => chunk.ChunkId))}",
                "cau hoi",
                4,
                TotalStageCount);
        }

        return bundles;
    }

    private static List<DocumentChunk> SelectEvidenceChunks(List<DocumentChunk> chunks, QuestionPlan plan)
    {
        var queryTokens = TokenizeForSearch($"{plan.TopicName} {plan.Subtopic} {plan.Focus} {plan.AnswerStyle}");
        var preferredIds = new HashSet<string>(plan.PreferredChunkIds, StringComparer.OrdinalIgnoreCase);

        var ranked = chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = ScoreChunk(plan, preferredIds, queryTokens, chunk)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.ChunkNumber)
            .Select(item => item.Chunk)
            .ToList();

        var selected = new List<DocumentChunk>();

        foreach (var preferredId in plan.PreferredChunkIds)
        {
            var preferredChunk = ranked.FirstOrDefault(chunk => string.Equals(chunk.ChunkId, preferredId, StringComparison.OrdinalIgnoreCase));
            if (preferredChunk != null && selected.All(chunk => !string.Equals(chunk.ChunkId, preferredChunk.ChunkId, StringComparison.OrdinalIgnoreCase)))
            {
                selected.Add(preferredChunk);
            }
        }

        foreach (var chunk in ranked)
        {
            if (selected.Count >= EvidenceChunkLimit)
            {
                break;
            }

            if (selected.Any(existing => string.Equals(existing.ChunkId, chunk.ChunkId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(chunk);
        }

        if (!selected.Any())
        {
            selected.Add(chunks[plan.PlanOrder % chunks.Count]);
        }

        return selected
            .OrderBy(chunk => chunk.ChunkNumber)
            .Take(EvidenceChunkLimit)
            .ToList();
    }

    private static int ScoreChunk(QuestionPlan plan, HashSet<string> preferredIds, HashSet<string> queryTokens, DocumentChunk chunk)
    {
        var score = 0;

        if (preferredIds.Contains(chunk.ChunkId))
        {
            score += 40;
        }

        if (string.Equals(plan.CoverageZone, chunk.Zone, StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }

        var overlap = queryTokens.Intersect(chunk.SearchTokens, StringComparer.OrdinalIgnoreCase).Count();
        score += overlap * 4;

        foreach (var fact in chunk.KeyFacts)
        {
            var factTokens = TokenizeForSearch(fact);
            score += factTokens.Intersect(queryTokens, StringComparer.OrdinalIgnoreCase).Count() * 2;
        }

        return score;
    }

    private Question? ConvertToQuestion(
        int documentId,
        QuestionType type,
        QuestionPlan plan,
        EvidenceBundle bundle,
        GeneratedQuestionData item)
    {
        if (string.IsNullOrWhiteSpace(item.QuestionText))
        {
            return null;
        }

        var evidenceIds = NormalizeEvidenceIds(item.EvidenceChunkIds, bundle);
        var explanation = BuildGroundedExplanation(item.Explanation, evidenceIds);
        var difficulty = ParseDifficulty(string.IsNullOrWhiteSpace(item.Difficulty) ? plan.Difficulty : item.Difficulty);
        var correctAnswer = string.IsNullOrWhiteSpace(item.CorrectAnswer)
            ? BuildFallbackCorrectAnswer(type, bundle)
            : item.CorrectAnswer!.Trim();

        var question = new Question
        {
            DocumentId = documentId,
            QuestionText = NormalizeQuestionText(type, item.QuestionText!),
            QuestionType = type,
            CorrectAnswer = correctAnswer,
            Explanation = explanation,
            Difficulty = difficulty,
            Topic = plan.TopicTag,
            CreatedAt = DateTime.UtcNow
        };

        switch (type)
        {
            case QuestionType.TrueFalse:
                question.CorrectAnswer = NormalizeTrueFalseAnswer(correctAnswer);
                question.SetOptions(BuildTrueFalseOptions(question.CorrectAnswer));
                break;
            case QuestionType.MultipleChoice:
                var options = NormalizeMultipleChoiceOptions(item.Options, correctAnswer);
                if (options.Count != 4 || options.Count(option => option.IsCorrect) != 1)
                {
                    return null;
                }

                question.CorrectAnswer = options.First(option => option.IsCorrect).Key;
                question.SetOptions(options);
                break;
            case QuestionType.FillInTheBlank:
                if (!question.QuestionText.Contains("_____", StringComparison.Ordinal))
                {
                    return null;
                }
                break;
            case QuestionType.ShortAnswer:
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
                {
                    return null;
                }
                break;
        }

        return question;
    }

    private async Task<Question?> FinalizeQuestionAsync(
        int documentId,
        QuestionType type,
        QuestionPlan plan,
        EvidenceBundle bundle,
        GeneratedQuestionData? initialItem,
        IReadOnlyCollection<Question> existingQuestions)
    {
        var currentItem = initialItem;
        var qualityIssues = new List<string>();

        for (var attempt = 0; attempt <= QuestionRetryLimit; attempt++)
        {
            var question = currentItem == null
                ? null
                : ConvertToQuestion(documentId, type, plan, bundle, currentItem);

            if (question != null)
            {
                question = await PolishQuestionAsync(type, plan, bundle, question);

                if (IsQuestionQualityAcceptable(question, type, existingQuestions, out qualityIssues))
                {
                    return question;
                }
            }
            else
            {
                qualityIssues = new List<string> { "Ket qua AI khong dung schema cau hoi hoac thieu truong bat buoc." };
            }

            if (attempt >= QuestionRetryLimit)
            {
                break;
            }

            currentItem = await RetryGenerateSingleQuestionAsync(type, plan, bundle, qualityIssues);
        }

        return null;
    }

    private async Task<Question> PolishQuestionAsync(
        QuestionType type,
        QuestionPlan plan,
        EvidenceBundle bundle,
        Question question)
    {
        try
        {
            var prompt = $@"Polish the learner-facing question below.

Question type: {type}
Topic tag: {plan.TopicTag}
Allowed evidence:
{BuildEvidenceBlock(bundle)}

Current question:
{BuildQuestionSnapshot(question)}

Requirements:
1. Keep the same factual meaning and the same correct answer.
2. Write natural, clean, concise Vietnamese for learners.
3. Remove OCR artifacts, awkward wording, duplicated words, and machine-like phrasing.
4. Do not add new facts outside the allowed evidence.
5. explanation must stay learner-friendly and may end with evidence refs in square brackets only when needed.
6. For multiple choice, keep exactly four options with keys A, B, C, D.
7. For true/false, keep the answer semantics unchanged.

Return JSON only:
{BuildQuestionPolishExample(type)}";

            var draft = await _ollamaService.GenerateStructuredResponseAsync<QuestionPolishDraft>(
                prompt,
                "You are a senior Vietnamese educational editor. Polish text without changing the answer or facts.",
                OllamaModelProfile.Generation);

            return ApplyPolishDraft(question, type, bundle, draft);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error polishing generated question for {PlanId}", plan.PlanId);
            return question;
        }
    }

    private async Task<GeneratedQuestionData?> RetryGenerateSingleQuestionAsync(
        QuestionType type,
        QuestionPlan plan,
        EvidenceBundle bundle,
        IReadOnlyList<string> issues)
    {
        try
        {
            var prompt = $@"Retry one grounded {type} question.

Allowed evidence:
{BuildEvidenceBlock(bundle)}

Question brief:
- planId: {plan.PlanId}
- topicName: {plan.TopicName}
- subtopic: {plan.Subtopic}
- focus: {plan.Focus}
- answerStyle: {plan.AnswerStyle}
- difficulty: {plan.Difficulty}
- allowedChunkIds: {string.Join(", ", bundle.EvidenceChunks.Select(chunk => chunk.ChunkId))}

Previous attempt issues:
- {string.Join("\n- ", issues.Where(issue => !string.IsNullOrWhiteSpace(issue)).DefaultIfEmpty("Cau hoi chua dat quality gate"))}

Requirements:
- Use only the allowed evidence.
- Make the question polished and learner-friendly on the first try.
- Avoid OCR artifacts, technical wording, fallback wording, and duplicate phrasing.
- topic must exactly equal {plan.TopicTag}.
- evidenceChunkIds must be a non-empty subset of the allowedChunkIds.
- explanation should be natural Vietnamese and may mention evidence ids only at the end.

{BuildTypeSpecificPrompt(type)}

Return JSON only:
{BuildTypeSpecificExample(type)}";

            var results = await _ollamaService.GenerateStructuredResponseAsync<List<GeneratedQuestionData>>(
                prompt,
                BuildGenerationSystemPrompt(type),
                OllamaModelProfile.Generation);

            return results?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrying question generation for {PlanId}", plan.PlanId);
            return null;
        }
    }

    private Question ApplyPolishDraft(
        Question question,
        QuestionType type,
        EvidenceBundle bundle,
        QuestionPolishDraft? draft)
    {
        if (draft == null)
        {
            return question;
        }

        question.QuestionText = NormalizeQuestionText(type, draft.QuestionText ?? question.QuestionText);
        question.Explanation = BuildGroundedExplanation(
            draft.Explanation ?? question.Explanation,
            bundle.EvidenceChunks.Select(chunk => chunk.ChunkId));

        switch (type)
        {
            case QuestionType.MultipleChoice:
                var polishedOptions = NormalizePolishedMultipleChoiceOptions(draft.Options, question.GetOptions(), question.CorrectAnswer ?? "A");
                if (polishedOptions.Count == 4)
                {
                    question.SetOptions(polishedOptions);
                }
                break;
            case QuestionType.ShortAnswer:
            case QuestionType.FillInTheBlank:
                if (!string.IsNullOrWhiteSpace(draft.CorrectAnswer))
                {
                    question.CorrectAnswer = TextCleanupUtility.NormalizeForDisplay(draft.CorrectAnswer);
                }
                break;
        }

        return question;
    }

    private static string BuildEvidenceBlock(EvidenceBundle bundle)
    {
        var builder = new StringBuilder();

        foreach (var chunk in bundle.EvidenceChunks)
        {
            builder.AppendLine($"[{chunk.ChunkId}] {chunk.Label} | zone={chunk.Zone}");
            builder.AppendLine($"Summary: {chunk.Summary}");

            if (chunk.KeyFacts.Any())
            {
                builder.AppendLine("Facts:");
                foreach (var fact in chunk.KeyFacts.Take(4))
                {
                    builder.AppendLine($"- {fact}");
                }
            }

            builder.AppendLine($"Excerpt: {chunk.EvidenceExcerpt}");
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string BuildQuestionSnapshot(Question question)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Question: {question.QuestionText}");
        builder.AppendLine($"CorrectAnswer: {question.CorrectAnswer}");
        builder.AppendLine($"Explanation: {question.Explanation}");

        var options = question.GetOptions();
        if (options.Any())
        {
            builder.AppendLine("Options:");
            foreach (var option in options.OrderBy(option => option.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {option.Key}. {option.Text} {(option.IsCorrect ? "(correct)" : string.Empty)}".TrimEnd());
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildQuestionPolishExample(QuestionType type)
        => type switch
        {
            QuestionType.TrueFalse => @"{
  ""questionText"": ""Menh de duoc viet tu nhien, ro nghia."",
  ""options"": [
    { ""key"": ""A"", ""text"": ""Dung"" },
    { ""key"": ""B"", ""text"": ""Sai"" }
  ],
  ""correctAnswer"": ""A"",
  ""explanation"": ""Giai thich ngan, tu nhien, bam sat can cu.""
}",
            QuestionType.ShortAnswer => @"{
  ""questionText"": ""Cau hoi tra loi ngan, ro y va de doc."",
  ""correctAnswer"": ""Cum tu ngan chinh xac"",
  ""explanation"": ""Giai thich ngan, tu nhien, bam sat can cu.""
}",
            QuestionType.FillInTheBlank => @"{
  ""questionText"": ""Noi dung co cho trong _____ de nguoi hoc dien vao."",
  ""correctAnswer"": ""Cum tu can dien"",
  ""explanation"": ""Giai thich ngan, tu nhien, bam sat can cu.""
}",
            _ => @"{
  ""questionText"": ""Cau hoi trac nghiem ro nghia, tu nhien, de doc."",
  ""options"": [
    { ""key"": ""A"", ""text"": ""Lua chon A"" },
    { ""key"": ""B"", ""text"": ""Lua chon B"" },
    { ""key"": ""C"", ""text"": ""Lua chon C"" },
    { ""key"": ""D"", ""text"": ""Lua chon D"" }
  ],
  ""correctAnswer"": ""B"",
  ""explanation"": ""Giai thich ngan, tu nhien, bam sat can cu.""
}"
        };

    private static bool IsQuestionQualityAcceptable(
        Question question,
        QuestionType type,
        IReadOnlyCollection<Question> existingQuestions,
        out List<string> issues)
    {
        issues = new List<string>();

        if (string.IsNullOrWhiteSpace(question.QuestionText))
        {
            issues.Add("Question text dang rong.");
        }
        else
        {
            if (question.QuestionText.Length < 18 || question.QuestionText.Length > 260)
            {
                issues.Add("Question text qua ngan hoac qua dai.");
            }

            if (TextCleanupUtility.HasNoisyArtifacts(question.QuestionText))
            {
                issues.Add("Question text con OCR artifact hoac cu phap may.");
            }

            if (Regex.IsMatch(question.QuestionText, @"\b(cau hoi du phong|grounded|chunk id|preferredchunk)\b", RegexOptions.IgnoreCase))
            {
                issues.Add("Question text con lo thong diep ky thuat.");
            }

            if (type != QuestionType.FillInTheBlank && !Regex.IsMatch(question.QuestionText, @"[?.!]$"))
            {
                issues.Add("Question text chua co dau cau ket thuc.");
            }
        }

        if (IsNearDuplicateQuestion(question.QuestionText, existingQuestions))
        {
            issues.Add("Question bi trung voi cau hoi da co.");
        }

        if (!string.IsNullOrWhiteSpace(question.Explanation))
        {
            if (TextCleanupUtility.HasNoisyArtifacts(question.Explanation))
            {
                issues.Add("Explanation con artifact hoac placeholder.");
            }

            if (Regex.IsMatch(question.Explanation, @"\b(cau hoi du phong|preferredchunk|chunk id)\b", RegexOptions.IgnoreCase))
            {
                issues.Add("Explanation con wording ky thuat.");
            }
        }

        switch (type)
        {
            case QuestionType.MultipleChoice:
                var options = question.GetOptions();
                if (options.Count != 4 || options.Count(option => option.IsCorrect) != 1)
                {
                    issues.Add("Multiple choice chua co dung 4 lua chon voi 1 dap an dung.");
                    break;
                }

                if (options.Any(option => string.IsNullOrWhiteSpace(option.Text) || TextCleanupUtility.HasNoisyArtifacts(option.Text)))
                {
                    issues.Add("Mot hoac nhieu lua chon van con artifact.");
                }

                if (options.Select(option => option.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 4)
                {
                    issues.Add("Lua chon bi trung nhau.");
                }
                break;
            case QuestionType.FillInTheBlank:
                if (!question.QuestionText.Contains("_____", StringComparison.Ordinal))
                {
                    issues.Add("Fill in the blank thieu cho trong.");
                }
                break;
            case QuestionType.ShortAnswer:
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer) || TextCleanupUtility.HasNoisyArtifacts(question.CorrectAnswer))
                {
                    issues.Add("Dap an ngan chua dat chat luong.");
                }
                break;
        }

        return issues.Count == 0;
    }

    private static void ApplyQuestionVerifierMetadata(
        Question question,
        QuestionType type,
        EvidenceBundle bundle,
        IReadOnlyCollection<Question> existingQuestions,
        bool usedFallback)
    {
        var score = 100;
        var warnings = new List<string>();

        void AddWarning(string message, int penalty)
        {
            if (warnings.Any(existing => string.Equals(existing, message, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            warnings.Add(message);
            score -= penalty;
        }

        if (usedFallback)
        {
            AddWarning("Cau hoi nay dang dung duong fallback vi AI chua tra ve ket qua grounded dat yeu cau.", 32);
        }

        if (string.IsNullOrWhiteSpace(question.QuestionText))
        {
            AddWarning("Question text dang rong.", 40);
        }
        else
        {
            if (question.QuestionText.Length < 28 || question.QuestionText.Length > 180)
            {
                AddWarning("Question text co do dai chua toi uu cho nguoi hoc.", 8);
            }

            if (TextCleanupUtility.HasNoisyArtifacts(question.QuestionText))
            {
                AddWarning("Question text con dau hieu OCR artifact hoac wording may.", 28);
            }

            if (question.QuestionText.StartsWith("Theo tai lieu", StringComparison.OrdinalIgnoreCase))
            {
                AddWarning("Cach dat cau hoi van con hoi chung chung.", 6);
            }
        }

        if (string.IsNullOrWhiteSpace(question.Explanation))
        {
            AddWarning("Explanation dang rong hoac qua ngan.", 14);
        }
        else
        {
            if (question.Explanation.Length < 40)
            {
                AddWarning("Explanation kha ngan, co the chua du ro.", 8);
            }

            if (!question.Explanation.Contains("[C", StringComparison.OrdinalIgnoreCase))
            {
                AddWarning("Explanation chua neo ro can cu evidence.", 6);
            }

            if (TextCleanupUtility.HasNoisyArtifacts(question.Explanation))
            {
                AddWarning("Explanation con artifact hoac thong diep ky thuat.", 18);
            }
        }

        if (string.Equals(question.Topic, DefaultTopicTag, StringComparison.OrdinalIgnoreCase))
        {
            AddWarning("Topic tag con o muc mac dinh, chua du dac trung.", 5);
        }

        if (bundle.EvidenceChunks.Count < 2)
        {
            AddWarning("Chi dang dua tren it evidence chunk, do phu co the chua cao.", 5);
        }

        if (IsNearDuplicateQuestion(question.QuestionText, existingQuestions))
        {
            AddWarning("Noi dung cau hoi kha gan voi mot cau hoi khac trong cung bo.", 18);
        }

        switch (type)
        {
            case QuestionType.MultipleChoice:
                var options = question.GetOptions();
                if (options.Count != 4)
                {
                    AddWarning("So lua chon trac nghiem chua dung 4.", 24);
                    break;
                }

                if (options.Any(option => option.Text.Length < 4))
                {
                    AddWarning("Mot vai lua chon qua ngan.", 8);
                }

                if (options.Any(option => TextCleanupUtility.HasNoisyArtifacts(option.Text)))
                {
                    AddWarning("Mot vai lua chon van con artifact.", 18);
                }

                if (options.Select(option => option.Text.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 4)
                {
                    AddWarning("Cac lua chon bi trung hoac qua giong nhau.", 16);
                }
                break;
            case QuestionType.TrueFalse:
                if (!string.Equals(question.CorrectAnswer, "A", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(question.CorrectAnswer, "B", StringComparison.OrdinalIgnoreCase))
                {
                    AddWarning("Dap an dung/sai chua on dinh.", 22);
                }
                break;
            case QuestionType.ShortAnswer:
            case QuestionType.FillInTheBlank:
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer) || question.CorrectAnswer.Length < 2)
                {
                    AddWarning("Dap an ngan co ve chua day du.", 18);
                }
                else if (TextCleanupUtility.HasNoisyArtifacts(question.CorrectAnswer))
                {
                    AddWarning("Dap an ngan con artifact.", 18);
                }
                break;
        }

        question.VerifierScore = Math.Clamp(score, 0, 100);
        question.SetVerifierIssues(warnings);
    }

    private static List<QuestionOption> NormalizePolishedMultipleChoiceOptions(
        List<QuestionOption>? polishedOptions,
        List<QuestionOption> fallbackOptions,
        string correctAnswer)
    {
        if (polishedOptions == null || polishedOptions.Count == 0)
        {
            return fallbackOptions;
        }

        var normalized = polishedOptions
            .OrderBy(option => NormalizeOptionKey(option.Key))
            .Take(4)
            .Select((option, index) => new QuestionOption
            {
                Key = ((char)('A' + index)).ToString(),
                Text = TrimForOption(option.Text),
                IsCorrect = false
            })
            .ToList();

        if (normalized.Count != 4)
        {
            return fallbackOptions;
        }

        var correctKey = NormalizeOptionKey(correctAnswer);
        var correctIndex = correctKey switch
        {
            "B" => 1,
            "C" => 2,
            "D" => 3,
            _ => 0
        };

        for (var index = 0; index < normalized.Count; index++)
        {
            normalized[index].IsCorrect = index == correctIndex;
        }

        return normalized;
    }

    private static List<QuestionPlan> NormalizeQuestionPlans(
        List<QuestionPlanDraft>? drafts,
        List<DocumentChunk> chunks,
        int count,
        QuestionType type)
    {
        if (drafts == null || !drafts.Any())
        {
            return BuildFallbackPlans(chunks, count, type);
        }

        var validChunkIds = new HashSet<string>(chunks.Select(chunk => chunk.ChunkId), StringComparer.OrdinalIgnoreCase);
        var plans = new List<QuestionPlan>(count);

        for (var index = 0; index < drafts.Count && plans.Count < count; index++)
        {
            var draft = drafts[index];
            var topicName = NormalizeLabel(draft.TopicName);
            var subtopic = NormalizeLabel(draft.Subtopic);
            var focus = NormalizeSentence(draft.Focus);
            if (string.IsNullOrWhiteSpace(topicName) || string.IsNullOrWhiteSpace(subtopic) || string.IsNullOrWhiteSpace(focus))
            {
                continue;
            }

            var preferredChunkIds = (draft.PreferredChunkIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim().ToUpperInvariant())
                .Where(validChunkIds.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(EvidenceChunkLimit)
                .ToList();

            if (!preferredChunkIds.Any())
            {
                preferredChunkIds.Add(chunks[index % chunks.Count].ChunkId);
            }

            var firstChunk = chunks.First(chunk => string.Equals(chunk.ChunkId, preferredChunkIds[0], StringComparison.OrdinalIgnoreCase));
            plans.Add(new QuestionPlan
            {
                PlanId = string.IsNullOrWhiteSpace(draft.PlanId) ? $"P{plans.Count + 1:00}" : draft.PlanId!.Trim().ToUpperInvariant(),
                PlanOrder = plans.Count,
                TopicName = topicName,
                Subtopic = subtopic,
                Focus = focus,
                AnswerStyle = NormalizeSentence(draft.AnswerStyle) ?? "nho kien thuc chinh",
                Difficulty = NormalizeDifficulty(draft.Difficulty),
                PreferredChunkIds = preferredChunkIds,
                CoverageZone = firstChunk.Zone,
                TopicTag = CreateTopicTag(topicName, subtopic)
            });
        }

        if (plans.Count < count)
        {
            plans.AddRange(BuildFallbackPlans(chunks, count - plans.Count, type, plans.Count));
        }

        return plans
            .Take(count)
            .Select((plan, index) => plan with
            {
                PlanOrder = index,
                PlanId = $"P{index + 1:00}"
            })
            .ToList();
    }

    private static List<QuestionPlan> BuildFallbackPlans(List<DocumentChunk> chunks, int count, QuestionType type, int startOrder = 0)
    {
        var plans = new List<QuestionPlan>(count);

        for (var index = 0; index < count; index++)
        {
            var chunk = chunks[(startOrder + index) % chunks.Count];
            var topicName = NormalizeLabel(chunk.Label) ?? "Noi dung trong tam";
            var subtopic = chunk.KeyFacts.FirstOrDefault(fact => !string.IsNullOrWhiteSpace(fact)) ?? "chi tiet quan trong";
            var focus = chunk.KeyFacts.FirstOrDefault(fact => !string.IsNullOrWhiteSpace(fact)) ?? $"Noi dung then chot cua {chunk.ChunkId}";

            plans.Add(new QuestionPlan
            {
                PlanId = $"P{startOrder + index + 1:00}",
                PlanOrder = startOrder + index,
                TopicName = topicName,
                Subtopic = NormalizeLabel(subtopic) ?? "chi tiet quan trong",
                Focus = NormalizeSentence(focus) ?? $"Noi dung then chot cua {chunk.ChunkId}",
                AnswerStyle = type switch
                {
                    QuestionType.TrueFalse => "xac dinh menh de dung hay sai",
                    QuestionType.ShortAnswer => "tra loi ngan bang mot cum tu ro rang",
                    QuestionType.FillInTheBlank => "dien thieu mot tu hoac cum tu chinh xac",
                    _ => "chon dap an dung nhat"
                },
                Difficulty = index % 3 == 0 ? "Easy" : index % 3 == 1 ? "Medium" : "Hard",
                PreferredChunkIds = new List<string> { chunk.ChunkId },
                CoverageZone = chunk.Zone,
                TopicTag = CreateTopicTag(topicName, subtopic)
            });
        }

        return plans;
    }

    private static List<Question> CreateFallbackQuestions(
        int documentId,
        int count,
        QuestionType questionType,
        List<QuestionPlan>? plans,
        List<EvidenceBundle>? bundles)
    {
        var questions = new List<Question>(count);
        var planList = plans ?? new List<QuestionPlan>();
        var bundleMap = bundles?
            .GroupBy(bundle => bundle.Plan.PlanId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, EvidenceBundle>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < count; index++)
        {
            var plan = planList.Count > index ? planList[index] : null;
            var bundle = plan != null && bundleMap.TryGetValue(plan.PlanId, out var found)
                ? found
                : plan != null
                    ? CreateFallbackBundle(plan)
                    : null;

            questions.Add(CreateFallbackQuestion(documentId, questionType, plan, bundle, index));
        }

        return questions;
    }

    private static Question CreateFallbackQuestion(
        int documentId,
        QuestionType questionType,
        QuestionPlan? plan,
        EvidenceBundle? bundle,
        int index)
    {
        var topicTag = plan?.TopicTag ?? DefaultTopicTag;
        var explanation = BuildGroundedExplanation(
            "Day la cau hoi du phong khi AI chua tra ve ket qua grounded hop le.",
            bundle?.EvidenceChunks.Select(chunk => chunk.ChunkId) ?? Array.Empty<string>());

        var question = new Question
        {
            DocumentId = documentId,
            QuestionText = BuildFallbackQuestionText(questionType, index + 1, plan),
            QuestionType = questionType,
            CorrectAnswer = BuildFallbackCorrectAnswer(questionType, bundle),
            Explanation = explanation,
            Difficulty = plan == null ? DifficultyLevel.Medium : ParseDifficulty(plan.Difficulty),
            Topic = topicTag,
            CreatedAt = DateTime.UtcNow
        };

        var options = BuildFallbackOptions(questionType, bundle);
        if (options.Any())
        {
            question.SetOptions(options);
            if (questionType == QuestionType.MultipleChoice)
            {
                question.CorrectAnswer = options.First(option => option.IsCorrect).Key;
            }
        }

        question.VerifierScore = 38;
        question.SetVerifierIssues(new List<string>
        {
            "Cau hoi nay dang dung duong fallback vi AI chua tra ve ket qua grounded dat yeu cau.",
            "Nen uu tien regenerate neu can bo cau hoi co do tin cay cao hon."
        });

        return question;
    }

    private static string BuildFallbackQuestionText(QuestionType questionType, int index, QuestionPlan? plan)
    {
        var focus = BuildFallbackQuestionFocus(plan);
        return questionType switch
        {
            QuestionType.TrueFalse => $"Menh de {index}: Theo tai lieu, nhan dinh sau ve {focus} la dung hay sai?",
            QuestionType.ShortAnswer => $"Cau {index}: Tra loi ngan ve {focus}.",
            QuestionType.FillInTheBlank => $"Cau {index}: Dien vao cho trong _____ lien quan den {focus}.",
            _ => $"Cau {index}: Theo tai lieu, nhan dinh nao mo ta dung nhat ve {focus}?"
        };
    }

    private static string BuildFallbackQuestionFocus(QuestionPlan? plan)
    {
        var focus = NormalizeLabel(plan?.Subtopic)
            ?? NormalizeLabel(plan?.TopicName)
            ?? NormalizeSentence(plan?.Focus)
            ?? "noi dung trong tai lieu";

        return Truncate(focus, 120);
    }

    private static string BuildFallbackCorrectAnswer(QuestionType questionType, EvidenceBundle? bundle)
    {
        var groundedAnswer = bundle?.EvidenceChunks
            .SelectMany(chunk => chunk.KeyFacts)
            .FirstOrDefault(fact => !string.IsNullOrWhiteSpace(fact));

        return questionType switch
        {
            QuestionType.TrueFalse => "A",
            QuestionType.ShortAnswer => groundedAnswer ?? "Cau tra loi nam trong tai lieu",
            QuestionType.FillInTheBlank => ExtractShortAnswerCandidate(groundedAnswer) ?? "Cum tu quan trong",
            _ => "A"
        };
    }

    private static List<QuestionOption> BuildFallbackOptions(QuestionType questionType, EvidenceBundle? bundle)
    {
        return questionType switch
        {
            QuestionType.TrueFalse => BuildTrueFalseOptions("A"),
            QuestionType.MultipleChoice => BuildFallbackMultipleChoiceOptions(bundle),
            _ => new List<QuestionOption>()
        };
    }

    private static List<QuestionOption> BuildFallbackMultipleChoiceOptions(EvidenceBundle? bundle)
    {
        var optionTexts = (bundle?.EvidenceChunks ?? new List<DocumentChunk>())
            .SelectMany(chunk => chunk.KeyFacts)
            .Where(fact => !string.IsNullOrWhiteSpace(fact))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Select(TrimForOption)
            .ToList();

        while (optionTexts.Count < 4)
        {
            optionTexts.Add($"Lua chon tham chieu {optionTexts.Count + 1}");
        }

        return new List<QuestionOption>
        {
            new() { Key = "A", Text = optionTexts[0], IsCorrect = true },
            new() { Key = "B", Text = optionTexts[1], IsCorrect = false },
            new() { Key = "C", Text = optionTexts[2], IsCorrect = false },
            new() { Key = "D", Text = optionTexts[3], IsCorrect = false }
        };
    }

    private static List<QuestionOption> NormalizeMultipleChoiceOptions(List<QuestionOption>? rawOptions, string correctAnswer)
    {
        if (rawOptions == null)
        {
            return new List<QuestionOption>();
        }

        var cleaned = rawOptions
            .Where(option => option != null && !string.IsNullOrWhiteSpace(option.Text))
            .Take(4)
            .Select((option, index) => new QuestionOption
            {
                Key = ((char)('A' + index)).ToString(),
                Text = TrimForOption(option.Text),
                IsCorrect = false
            })
            .ToList();

        if (cleaned.Count != 4)
        {
            return new List<QuestionOption>();
        }

        var rawCorrectKey = NormalizeOptionKey(correctAnswer);
        var resolvedIndex = rawCorrectKey switch
        {
            "A" => 0,
            "B" => 1,
            "C" => 2,
            "D" => 3,
            _ => rawOptions.FindIndex(option => option.IsCorrect)
        };

        if (resolvedIndex < 0 || resolvedIndex >= cleaned.Count)
        {
            resolvedIndex = 0;
        }

        for (var index = 0; index < cleaned.Count; index++)
        {
            cleaned[index].IsCorrect = index == resolvedIndex;
        }

        return cleaned;
    }

    private static List<QuestionOption> BuildTrueFalseOptions(string correctAnswer)
    {
        var normalized = NormalizeTrueFalseAnswer(correctAnswer);
        return new List<QuestionOption>
        {
            new() { Key = "A", Text = "Dung", IsCorrect = normalized == "A" },
            new() { Key = "B", Text = "Sai", IsCorrect = normalized == "B" }
        };
    }

    private static string NormalizeTrueFalseAnswer(string correctAnswer)
    {
        if (string.IsNullOrWhiteSpace(correctAnswer))
        {
            return "A";
        }

        var normalized = correctAnswer.Trim().ToLowerInvariant();
        return normalized switch
        {
            "b" => "B",
            "sai" => "B",
            "false" => "B",
            _ => "A"
        };
    }

    private static string NormalizeQuestionText(QuestionType type, string questionText)
    {
        var normalized = NormalizeSentence(questionText) ?? string.Empty;
        if (type == QuestionType.FillInTheBlank && !normalized.Contains("_____", StringComparison.Ordinal))
        {
            normalized = $"{normalized} _____";
        }

        if (type != QuestionType.FillInTheBlank && !string.IsNullOrWhiteSpace(normalized) && !Regex.IsMatch(normalized, @"[.!?]$"))
        {
            normalized += type == QuestionType.ShortAnswer ? "." : "?";
        }

        return normalized;
    }

    private static List<string> NormalizeEvidenceIds(List<string>? evidenceChunkIds, EvidenceBundle bundle)
    {
        var allowed = new HashSet<string>(bundle.EvidenceChunks.Select(chunk => chunk.ChunkId), StringComparer.OrdinalIgnoreCase);
        var evidenceIds = (evidenceChunkIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim().ToUpperInvariant())
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!evidenceIds.Any())
        {
            evidenceIds.Add(bundle.EvidenceChunks.First().ChunkId);
        }

        return evidenceIds;
    }

    private static string BuildGroundedExplanation(string? explanation, IEnumerable<string> evidenceIds)
    {
        var normalizedExplanation = NormalizeSentence(explanation) ?? "Cau hoi nay duoc doi chieu truc tiep voi evidence trong tai lieu.";
        if (normalizedExplanation.Contains("cau hoi du phong", StringComparison.OrdinalIgnoreCase))
        {
            normalizedExplanation = "Cau hoi nay duoc tao tu cac y chinh trong tai lieu va da duoc doi chieu voi evidence lien quan.";
        }

        var evidenceBlock = string.Join(", ", evidenceIds.Select(id => $"[{id}]"));

        if (string.IsNullOrWhiteSpace(evidenceBlock))
        {
            return normalizedExplanation;
        }

        if (normalizedExplanation.Contains("[C", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedExplanation;
        }

        return $"{normalizedExplanation} Can cu: {evidenceBlock}.";
    }

    private static string RenderCoverageMapForPrompt(List<DocumentChunk> chunks)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Tong cong {chunks.Count} chunk, doc theo thu tu tu dau den cuoi tai lieu.");
        builder.AppendLine();

        foreach (var chunk in chunks)
        {
            builder.AppendLine($"[{chunk.ChunkId}] Zone: {chunk.Zone}");
            builder.AppendLine($"Label: {chunk.Label}");
            builder.AppendLine($"Summary: {chunk.Summary}");
            builder.AppendLine("Study facts:");
            foreach (var fact in chunk.KeyFacts)
            {
                builder.AppendLine($"- {fact}");
            }
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string BuildEvidenceLibraryBlock(List<EvidenceBundle> bundles)
    {
        var uniqueChunks = bundles
            .SelectMany(bundle => bundle.EvidenceChunks)
            .GroupBy(chunk => chunk.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();

        var builder = new StringBuilder();
        foreach (var chunk in uniqueChunks)
        {
            builder.AppendLine($"[{chunk.ChunkId}] {chunk.Label} | zone={chunk.Zone}");
            builder.AppendLine($"Summary: {chunk.Summary}");
            builder.AppendLine("Facts:");
            foreach (var fact in chunk.KeyFacts.Take(3))
            {
                builder.AppendLine($"- {fact}");
            }
            builder.AppendLine($"Excerpt: {chunk.EvidenceExcerpt}");
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string BuildQuestionBriefBlock(List<QuestionPlan> plans, List<EvidenceBundle> bundles)
    {
        var bundleMap = bundles.ToDictionary(bundle => bundle.Plan.PlanId, StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();

        foreach (var plan in plans)
        {
            var evidenceIds = bundleMap.TryGetValue(plan.PlanId, out var bundle)
                ? string.Join(", ", bundle.EvidenceChunks.Select(chunk => chunk.ChunkId))
                : string.Join(", ", plan.PreferredChunkIds);

            builder.AppendLine($"[{plan.PlanId}]");
            builder.AppendLine($"topicTag: {plan.TopicTag}");
            builder.AppendLine($"topicName: {plan.TopicName}");
            builder.AppendLine($"subtopic: {plan.Subtopic}");
            builder.AppendLine($"focus: {plan.Focus}");
            builder.AppendLine($"difficulty: {plan.Difficulty}");
            builder.AppendLine($"answerStyle: {plan.AnswerStyle}");
            builder.AppendLine($"allowedEvidenceChunkIds: {evidenceIds}");
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string BuildAnalyzedContentBlock(ProcessedContent? processedContent)
    {
        if (processedContent == null)
        {
            return "- Khong co phan tich san. Uu tien coverage map va evidence truc tiep.";
        }

        var topics = processedContent.MainTopics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Take(8)
            .ToList();
        var keyPoints = processedContent.KeyPoints
            .Where(point => !string.IsNullOrWhiteSpace(point))
            .Take(12)
            .ToList();
        var summary = NormalizeSentence(processedContent.Summary) ?? "Khong co tom tat san.";

        var builder = new StringBuilder();
        builder.AppendLine($"Summary: {summary}");
        builder.AppendLine("Main topics:");
        if (topics.Any())
        {
            for (var index = 0; index < topics.Count; index++)
            {
                builder.AppendLine($"{index + 1}. {topics[index]}");
            }
        }
        else
        {
            builder.AppendLine("- Khong co main topic san.");
        }

        builder.AppendLine("Priority key points:");
        if (keyPoints.Any())
        {
            for (var index = 0; index < keyPoints.Count; index++)
            {
                builder.AppendLine($"{index + 1}. {keyPoints[index]}");
            }
        }
        else
        {
            builder.AppendLine("- Khong co key point san.");
        }

        return builder.ToString().Trim();
    }

    private static string BuildPlanningSystemPrompt(QuestionType type)
        => $"You are a senior assessment planner for grounded {type} questions. Cover the whole document, avoid invented facts, and return strict JSON only.";

    private static string BuildGenerationSystemPrompt(QuestionType type)
        => $"You are a senior educational assessment writer for grounded {type} questions. Use only provided evidence, never invent facts, and return strict JSON only. Write in {OutputLanguage}.";

    private static string BuildTypeSpecificPrompt(QuestionType type)
        => type switch
        {
            QuestionType.TrueFalse => @"Type-specific rules:
- Each questionText should be a single factual statement for learners to judge.
- options must contain exactly two items: A = Dung, B = Sai.
- correctAnswer must be ""A"" or ""B"".
- False statements must contradict a specific supported fact, not rely on wording tricks.",
            QuestionType.ShortAnswer => @"Type-specific rules:
- Each question requires one concise short answer.
- Prefer definitions, causes, conditions, names, formulas, dates, or explicit steps.
- Do not create opinion or open-ended essay prompts.",
            QuestionType.FillInTheBlank => @"Type-specific rules:
- questionText must contain exactly one blank token: _____
- correctAnswer must be a short word, phrase, number, or expression explicitly supported by evidence.",
            _ => @"Type-specific rules:
- options must contain exactly four items with keys A, B, C, D.
- Exactly one option is correct.
- Distractors must be plausible but wrong according to the evidence.
- Do not use 'all of the above' or 'none of the above'."
        };

    private static string BuildTypeSpecificExample(QuestionType type)
        => type switch
        {
            QuestionType.TrueFalse => @"[
  {
    ""planId"": ""P01"",
    ""questionText"": ""Menh de can danh gia"",
    ""options"": [
      { ""key"": ""A"", ""text"": ""Dung"", ""isCorrect"": true },
      { ""key"": ""B"", ""text"": ""Sai"", ""isCorrect"": false }
    ],
    ""correctAnswer"": ""A"",
    ""explanation"": ""Giai thich ngan co trich dan [C01]."",
    ""difficulty"": ""Medium"",
    ""topic"": ""topic-tag-tu-brief"",
    ""evidenceChunkIds"": [""C01""]
  }
]",
            QuestionType.ShortAnswer => @"[
  {
    ""planId"": ""P01"",
    ""questionText"": ""Cau hoi tra loi ngan"",
    ""correctAnswer"": ""Dap an ngan"",
    ""explanation"": ""Giai thich ngan co trich dan [C02]."",
    ""difficulty"": ""Medium"",
    ""topic"": ""topic-tag-tu-brief"",
    ""evidenceChunkIds"": [""C02""]
  }
]",
            QuestionType.FillInTheBlank => @"[
  {
    ""planId"": ""P01"",
    ""questionText"": ""Noi dung co cho trong _____ de dien"",
    ""correctAnswer"": ""Cum tu can dien"",
    ""explanation"": ""Giai thich ngan co trich dan [C03]."",
    ""difficulty"": ""Medium"",
    ""topic"": ""topic-tag-tu-brief"",
    ""evidenceChunkIds"": [""C03""]
  }
]",
            _ => @"[
  {
    ""planId"": ""P01"",
    ""questionText"": ""Cau hoi trac nghiem cu the"",
    ""options"": [
      { ""key"": ""A"", ""text"": ""Lua chon A"", ""isCorrect"": false },
      { ""key"": ""B"", ""text"": ""Lua chon B"", ""isCorrect"": true },
      { ""key"": ""C"", ""text"": ""Lua chon C"", ""isCorrect"": false },
      { ""key"": ""D"", ""text"": ""Lua chon D"", ""isCorrect"": false }
    ],
    ""correctAnswer"": ""B"",
    ""explanation"": ""Giai thich ngan co trich dan [C01], [C02]."",
    ""difficulty"": ""Medium"",
    ""topic"": ""topic-tag-tu-brief"",
    ""evidenceChunkIds"": [""C01"", ""C02""]
  }
]"
        };

    private static string NormalizeDocumentContent(string content)
        => string.IsNullOrWhiteSpace(content)
            ? string.Empty
            : TextCleanupUtility.NormalizeForAi(content, preserveLineBreaks: true);

    private static List<string> SplitIntoChunks(string content, int chunkSize, int overlap)
    {
        var pageChunks = SplitIntoPageChunks(content, chunkSize);
        if (pageChunks.Count > 1)
        {
            return pageChunks;
        }

        var chunks = new List<string>();
        var start = 0;

        while (start < content.Length)
        {
            var length = Math.Min(chunkSize, content.Length - start);
            var end = start + length;

            if (end < content.Length)
            {
                var searchWindow = Math.Min(length, 1000);
                var paragraphBreak = content.LastIndexOf("\n\n", end, searchWindow);
                if (paragraphBreak > start + (chunkSize / 2))
                {
                    end = paragraphBreak;
                    length = end - start;
                }
            }

            var chunk = content.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            if (end >= content.Length)
            {
                break;
            }

            start = Math.Max(end - overlap, start + 1);
        }

        return chunks;
    }

    private static List<string> SplitIntoPageChunks(string content, int chunkSize)
    {
        var pages = Regex.Split(content, @"(?=\[Page\s+\d+\])", RegexOptions.IgnoreCase)
            .Select(page => page.Trim())
            .Where(page => !string.IsNullOrWhiteSpace(page))
            .ToList();

        if (pages.Count <= 1)
        {
            return new List<string>();
        }

        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            if (builder.Length > 0 && builder.Length + page.Length > chunkSize)
            {
                chunks.Add(builder.ToString().Trim());
                builder.Clear();
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(page);
        }

        if (builder.Length > 0)
        {
            chunks.Add(builder.ToString().Trim());
        }

        return chunks;
    }

    private static string BuildChunkId(int chunkNumber) => $"C{chunkNumber:00}";

    private static string ResolveCoverageZone(int chunkNumber, int totalChunks)
    {
        if (totalChunks <= 2)
        {
            return chunkNumber == 1 ? "dau" : "cuoi";
        }

        var ratio = chunkNumber / (double)Math.Max(1, totalChunks);
        return ratio <= 0.34d ? "dau" : ratio <= 0.67d ? "giua" : "cuoi";
    }

    private static string BuildChunkLabel(string chunkText, int chunkNumber, int totalChunks)
    {
        var candidate = chunkText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("[Page", StringComparison.OrdinalIgnoreCase) && line.Length >= 12);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = ExtractHighSignalSentences(chunkText, 1).FirstOrDefault() ?? $"Phan {chunkNumber}/{totalChunks}";
        }

        return Truncate(candidate, 90);
    }

    private static string BuildChunkSummary(string chunkText, List<string> keyFacts)
    {
        if (keyFacts.Any())
        {
            return string.Join(" ", keyFacts.Take(2));
        }

        var excerpt = BuildEvidenceExcerpt(chunkText, keyFacts);
        return Truncate(excerpt, 220);
    }

    private static string BuildEvidenceExcerpt(string chunkText, List<string> keyFacts)
    {
        if (keyFacts.Any())
        {
            return Truncate(string.Join(" ", keyFacts.Take(3)), EvidenceExcerptLength);
        }

        var cleaned = Regex.Replace(chunkText.Replace('\n', ' '), @"\s+", " ").Trim();
        return Truncate(cleaned, EvidenceExcerptLength);
    }

    private static List<string> ExtractHighSignalSentences(string text, int maxCount)
    {
        var candidates = Regex.Split(text, @"(?<=[\.\?\!])\s+|\n+")
            .Select(sentence => Regex.Replace(sentence, @"\s+", " ").Trim())
            .Where(sentence =>
                !string.IsNullOrWhiteSpace(sentence) &&
                !sentence.StartsWith("[Page", StringComparison.OrdinalIgnoreCase) &&
                sentence.Length >= 18)
            .Select(sentence => new
            {
                Sentence = sentence,
                Score = ScoreSentence(sentence)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Sentence.Length)
            .ToList();

        var selected = new List<string>();
        foreach (var candidate in candidates)
        {
            if (selected.Count >= maxCount)
            {
                break;
            }

            var normalized = NormalizeSentence(candidate.Sentence) ?? candidate.Sentence;
            if (selected.Any(existing => string.Equals(NormalizeSentence(existing), normalized, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(Truncate(candidate.Sentence, 220));
        }

        return selected;
    }

    private static int ScoreSentence(string sentence)
    {
        var score = 0;
        var wordCount = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (wordCount is >= 6 and <= 28)
        {
            score += 6;
        }

        if (sentence.Any(char.IsDigit))
        {
            score += 4;
        }

        if (sentence.Contains(':', StringComparison.Ordinal))
        {
            score += 3;
        }

        if (sentence.Contains("la ", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("gom", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("bao gom", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("nguyen nhan", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("ket qua", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("buoc", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
    }

    private static HashSet<string> BuildSearchTokens(string chunkText, string summary, List<string> keyFacts)
        => TokenizeForSearch($"{summary} {string.Join(" ", keyFacts)} {Truncate(chunkText, 1600)}");

    private static HashSet<string> TokenizeForSearch(string value)
    {
        var normalized = NormalizeTagToken(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return normalized
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateTopicTag(string topicName, string subtopic)
    {
        var topicToken = NormalizeTagToken(topicName);
        var subtopicToken = NormalizeTagToken(subtopic);

        if (string.IsNullOrWhiteSpace(topicToken))
        {
            topicToken = "chu-de";
        }

        if (string.IsNullOrWhiteSpace(subtopicToken))
        {
            subtopicToken = "noi-dung";
        }

        var tag = $"{topicToken}:{subtopicToken}";
        return tag.Length <= 180 ? tag : tag[..180];
    }

    private static string NormalizeTagToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is ':' or '-' or '_' or ' ' or '/' or '|')
            {
                builder.Append('-');
            }
        }

        var collapsed = builder.ToString();
        while (collapsed.Contains("--", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("--", "-", StringComparison.Ordinal);
        }

        return collapsed.Trim('-');
    }

    private static string? NormalizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = TextCleanupUtility.NormalizeForDisplay(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : Truncate(normalized, 80);
    }

    private static string? NormalizeSentence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = TextCleanupUtility.NormalizeForDisplay(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : Truncate(normalized, 260);
    }

    private static string NormalizeDifficulty(string? difficulty)
        => difficulty?.Trim().ToLowerInvariant() switch
        {
            "easy" => "Easy",
            "hard" => "Hard",
            _ => "Medium"
        };

    private static DifficultyLevel ParseDifficulty(string? difficulty)
        => difficulty?.ToLowerInvariant() switch
        {
            "easy" => DifficultyLevel.Easy,
            "hard" => DifficultyLevel.Hard,
            _ => DifficultyLevel.Medium
        };

    private static string NormalizeOptionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().ToUpperInvariant();
        return trimmed.Length > 0 ? trimmed[..1] : string.Empty;
    }

    private static string TrimForOption(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Lua chon tham chieu";
        }

        return Truncate(TextCleanupUtility.NormalizeForDisplay(text), 120);
    }

    private static string? ExtractShortAnswerCandidate(string? fact)
    {
        if (string.IsNullOrWhiteSpace(fact))
        {
            return null;
        }

        var cleaned = TextCleanupUtility.NormalizeForDisplay(fact);
        if (cleaned.Contains(':', StringComparison.Ordinal))
        {
            cleaned = cleaned.Split(':', 2)[1].Trim();
        }

        return Truncate(cleaned, 80);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";

    private static bool IsNearDuplicateQuestion(string questionText, IEnumerable<Question> existingQuestions)
    {
        var normalizedCandidate = NormalizeTagToken(questionText);
        if (string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return false;
        }

        return existingQuestions.Any(existing =>
        {
            var normalizedExisting = NormalizeTagToken(existing.QuestionText);
            if (string.IsNullOrWhiteSpace(normalizedExisting))
            {
                return false;
            }

            if (string.Equals(normalizedCandidate, normalizedExisting, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var candidateTokens = normalizedCandidate.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var existingTokens = normalizedExisting.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (candidateTokens.Length == 0 || existingTokens.Length == 0)
            {
                return false;
            }

            var overlap = candidateTokens.Intersect(existingTokens, StringComparer.OrdinalIgnoreCase).Count();
            var minLength = Math.Min(candidateTokens.Length, existingTokens.Length);
            return overlap >= Math.Max(4, (int)Math.Round(minLength * 0.75d));
        });
    }

    private static int MapProgress(int startPercent, int endPercent, int current, int total)
    {
        if (total <= 0)
        {
            return endPercent;
        }

        var ratio = Math.Clamp(current / (double)total, 0d, 1d);
        return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
    }

    private static void ReportProgress(
        IProgress<QuestionGenerationProgressUpdate>? progress,
        int percent,
        string stage,
        string message,
        int? current = null,
        int? total = null,
        string? topicTag = null,
        string? stageLabel = null,
        string? detail = null,
        string? unitLabel = null,
        int? stageIndex = null,
        int? stageCount = null)
    {
        progress?.Report(new QuestionGenerationProgressUpdate
        {
            Percent = Math.Clamp(percent, 0, 100),
            Stage = stage,
            StageLabel = stageLabel,
            Message = message,
            Detail = detail,
            Current = current,
            Total = total,
            UnitLabel = unitLabel,
            StageIndex = stageIndex,
            StageCount = stageCount,
            TopicTag = topicTag
        });
    }

    private static EvidenceBundle CreateFallbackBundle(QuestionPlan plan)
    {
        var chunk = new DocumentChunk
        {
            ChunkNumber = plan.PlanOrder + 1,
            ChunkId = plan.PreferredChunkIds.FirstOrDefault() ?? BuildChunkId(plan.PlanOrder + 1),
            Zone = plan.CoverageZone,
            Label = plan.TopicName,
            Summary = plan.Focus,
            KeyFacts = new List<string> { plan.Focus },
            EvidenceExcerpt = plan.Focus,
            SearchTokens = TokenizeForSearch(plan.Focus)
        };

        return new EvidenceBundle
        {
            Plan = plan,
            EvidenceChunks = new List<DocumentChunk> { chunk }
        };
    }

    private sealed class DocumentChunk
    {
        public int ChunkNumber { get; init; }
        public string ChunkId { get; init; } = string.Empty;
        public string Zone { get; init; } = "giua";
        public string Label { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public List<string> KeyFacts { get; init; } = new();
        public string EvidenceExcerpt { get; init; } = string.Empty;
        public HashSet<string> SearchTokens { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class QuestionPlanDraft
    {
        public string? PlanId { get; set; }
        public string? TopicName { get; set; }
        public string? Subtopic { get; set; }
        public string? Focus { get; set; }
        public List<string>? PreferredChunkIds { get; set; }
        public string? AnswerStyle { get; set; }
        public string? Difficulty { get; set; }
    }

    private sealed record QuestionPlan
    {
        public string PlanId { get; init; } = string.Empty;
        public int PlanOrder { get; init; }
        public string TopicName { get; init; } = string.Empty;
        public string Subtopic { get; init; } = string.Empty;
        public string Focus { get; init; } = string.Empty;
        public string AnswerStyle { get; init; } = string.Empty;
        public string Difficulty { get; init; } = "Medium";
        public List<string> PreferredChunkIds { get; init; } = new();
        public string CoverageZone { get; init; } = "giua";
        public string TopicTag { get; init; } = DefaultTopicTag;
    }

    private sealed class EvidenceBundle
    {
        public QuestionPlan Plan { get; init; } = new();
        public List<DocumentChunk> EvidenceChunks { get; init; } = new();
    }

    private sealed class GeneratedQuestionData
    {
        public string? PlanId { get; set; }
        public string? QuestionText { get; set; }
        public List<QuestionOption>? Options { get; set; }
        public string? CorrectAnswer { get; set; }
        public string? Explanation { get; set; }
        public string? Difficulty { get; set; }
        public string? Topic { get; set; }
        public List<string>? EvidenceChunkIds { get; set; }
    }

    private sealed class QuestionPolishDraft
    {
        public string? QuestionText { get; set; }
        public List<QuestionOption>? Options { get; set; }
        public string? CorrectAnswer { get; set; }
        public string? Explanation { get; set; }
    }
}
