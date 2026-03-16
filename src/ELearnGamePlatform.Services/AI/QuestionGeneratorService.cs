using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ELearnGamePlatform.Services.AI;

public class QuestionGeneratorService : IQuestionGenerator
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<QuestionGeneratorService> _logger;
    private const string OutputLanguage = "Vietnamese";
    private const int ChunkSize = 5000;
    private const int ChunkOverlap = 400;
    private const int MaxChunksForQuestioningContext = 4;
    private const string DefaultTopicTag = "noi-dung-trong-tam:khai-niem-chinh";

    public QuestionGeneratorService(IOllamaService ollamaService, ILogger<QuestionGeneratorService> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task<List<Question>> GenerateQuestionsAsync(int documentId, string content, int count = 10, IProgress<QuestionGenerationProgressUpdate>? progress = null)
    {
        try
        {
            ReportProgress(progress, 5, "preparing-context", "Đang chuẩn bị ngữ cảnh tài liệu");
            var preparedContent = await BuildDocumentQuestioningContextAsync(content);
            ReportProgress(progress, 20, "building-topic-guidance", "Đang xây dựng topic guidance");
            var topicGuidance = await BuildTopicGuidanceAsync(preparedContent, count);
            var topicTagCatalog = BuildTopicTagCatalog(topicGuidance);
            var keyPointsForQuestions = ExtractCriticalFacts(topicGuidance, Math.Max(6, Math.Min(12, count * 2)));
            var systemPrompt = "You are an expert educational content creator. Generate high-quality quiz questions based on the provided content. Always write the final result in Vietnamese.";
            var generationStopwatch = Stopwatch.StartNew();
            ReportProgress(progress, 35, "sending-generation-prompt", "Đang gửi yêu cầu sinh câu hỏi tới AI");
            
            var prompt = $@"Analyze the following content and create {count} diverse multiple choice questions that test understanding of key concepts.

Document context compiled from the full document:
{preparedContent}

Topic analysis for this document (must be followed):
{topicGuidance}

Allowed TOPIC_TAG catalog (must choose exact value for each question.topic):
{BuildTopicTagPromptBlock(topicTagCatalog)}

Priority key points for question generation (must prioritize these facts):
{BuildKeyPointsPromptBlock(keyPointsForQuestions)}

Requirements:
- Each question must have exactly 4 options (A, B, C, D)
- Only one correct answer per question
- Questions should test different aspects of the content
- Include clear explanations for correct answers
- Vary difficulty levels (Easy, Medium, Hard)
- Translate and write the question text, option text, explanation, and topic in Vietnamese
- Keep the answer keys as A, B, C, D
- Use the provided topic analysis and distribute questions across those topics as evenly as possible
- Set the topic field as an exact TOPIC_TAG from the allowed catalog above (format: main-topic:subtopic-tag)
- Do not invent new tags, do not use free-text topic labels outside the catalog
- Prefer factual accuracy over creativity
- Avoid duplicate or near-duplicate questions
- If the document has sections or chapters, cover multiple sections instead of only the beginning
- At least 80% of questions should directly test one of the priority key points above
- In explanation, mention the key fact used to answer the question

Respond with a valid JSON array only (no markdown, no extra text):
[
{{
""questionText"": ""Clear and specific question text?"",
""options"": [
{{""key"": ""A"", ""text"": ""First option"", ""isCorrect"": false}},
{{""key"": ""B"", ""text"": ""Second option"", ""isCorrect"": true}},
{{""key"": ""C"", ""text"": ""Third option"", ""isCorrect"": false}},
{{""key"": ""D"", ""text"": ""Fourth option"", ""isCorrect"": false}}
],
""correctAnswer"": ""B"",
""explanation"": ""Explanation why B is correct"",
""difficulty"": ""Medium"",
""topic"": ""chu-de-chinh:y-nho-cu-the""
}}
]";

            _logger.LogInformation("Sending prompt to Ollama for question generation. Original content length: {OriginalLength}, Prepared content length: {PreparedLength}, Requested count: {Count}, KeyPoints: {KeyPointsCount}", content.Length, preparedContent.Length, count, keyPointsForQuestions.Count);
            
            var questionsData = await _ollamaService.GenerateStructuredResponseAsync<List<QuestionData>>(prompt, systemPrompt);
            
            if (questionsData == null || !questionsData.Any())
            {
                _logger.LogWarning("Failed to generate questions from AI - received null or empty response. Using fallback questions.");
                ReportProgress(progress, 85, "fallback", "AI không trả về dữ liệu hợp lệ, chuyển sang fallback");
                ReportProgress(progress, 100, "completed", "Hoàn tất bằng fallback");
                return CreateFallbackQuestions(documentId, count, QuestionType.MultipleChoice, topicTagCatalog);
            }

            _logger.LogInformation("Successfully generated {Count} questions from AI in {ElapsedMs} ms", questionsData.Count, generationStopwatch.ElapsedMilliseconds);
            
            return questionsData.Select((q, index) =>
            {
                var normalizedTopicTag = NormalizeTopicTag(q.Topic, topicTagCatalog, index);
                _logger.LogInformation("Question generation progress {Current}/{Total}: topic-tag={TopicTag}", index + 1, questionsData.Count, normalizedTopicTag);
                ReportProgress(progress,
                    40 + (int)Math.Round(((index + 1d) / questionsData.Count) * 55d),
                    "processing-questions",
                    $"Đang xử lý câu hỏi {index + 1}/{questionsData.Count}",
                    index + 1,
                    questionsData.Count,
                    normalizedTopicTag);

                var question = new Question
                {
                    DocumentId = documentId,
                    QuestionText = q.QuestionText,
                    QuestionType = QuestionType.MultipleChoice,
                    CorrectAnswer = q.CorrectAnswer,
                    Explanation = q.Explanation,
                    Difficulty = ParseDifficulty(q.Difficulty),
                    Topic = normalizedTopicTag,
                    CreatedAt = DateTime.UtcNow
                };
                question.SetOptions(q.Options ?? new List<QuestionOption>());
                return question;
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating questions");
            ReportProgress(progress, 100, "failed", $"Lỗi sinh câu hỏi: {ex.Message}");
            return CreateFallbackQuestions(documentId, count, QuestionType.MultipleChoice, topicTagCatalog: null);
        }
        finally
        {
            ReportProgress(progress, 100, "completed", "Hoàn tất sinh câu hỏi");
        }
    }

    public async Task<List<Question>> GenerateQuestionsByTypeAsync(int documentId, string content, QuestionType type, int count = 10, IProgress<QuestionGenerationProgressUpdate>? progress = null)
    {
        try
        {
            ReportProgress(progress, 5, "preparing-context", "Đang chuẩn bị ngữ cảnh tài liệu");
            var systemPrompt = $"You are an expert at creating {type} questions for educational content. Always write the final result in {OutputLanguage}.";
            var preparedContent = await BuildDocumentQuestioningContextAsync(content);
            ReportProgress(progress, 20, "building-topic-guidance", "Đang xây dựng topic guidance");
            var topicGuidance = await BuildTopicGuidanceAsync(preparedContent, count);
            var topicTagCatalog = BuildTopicTagCatalog(topicGuidance);
            var keyPointsForQuestions = ExtractCriticalFacts(topicGuidance, Math.Max(6, Math.Min(12, count * 2)));
            
            string prompt = type switch
            {
                QuestionType.TrueFalse => GenerateTrueFalsePrompt(preparedContent, topicGuidance, topicTagCatalog, keyPointsForQuestions, count),
                QuestionType.ShortAnswer => GenerateShortAnswerPrompt(preparedContent, topicGuidance, topicTagCatalog, keyPointsForQuestions, count),
                QuestionType.FillInTheBlank => GenerateFillInBlankPrompt(preparedContent, topicGuidance, topicTagCatalog, keyPointsForQuestions, count),
                _ => GenerateMultipleChoicePrompt(preparedContent, topicGuidance, topicTagCatalog, keyPointsForQuestions, count)
            };

            ReportProgress(progress, 35, "sending-generation-prompt", "Đang gửi yêu cầu sinh câu hỏi tới AI");
            var questionsData = await _ollamaService.GenerateStructuredResponseAsync<List<QuestionData>>(prompt, systemPrompt);
            
            if (questionsData == null || !questionsData.Any())
            {
                _logger.LogWarning("Failed to generate {QuestionType} questions from AI - received null or empty response. Using typed fallback questions.", type);
                ReportProgress(progress, 85, "fallback", "AI không trả về dữ liệu hợp lệ, chuyển sang fallback");
                ReportProgress(progress, 100, "completed", "Hoàn tất bằng fallback");
                return CreateFallbackQuestions(documentId, count, type, topicTagCatalog);
            }

            return questionsData.Select((q, index) =>
            {
                var normalizedTopicTag = NormalizeTopicTag(q.Topic, topicTagCatalog, index);
                _logger.LogInformation("Question generation progress {Current}/{Total} [{Type}]: topic-tag={TopicTag}", index + 1, questionsData.Count, type, normalizedTopicTag);
                ReportProgress(progress,
                    40 + (int)Math.Round(((index + 1d) / questionsData.Count) * 55d),
                    "processing-questions",
                    $"Đang xử lý câu hỏi {index + 1}/{questionsData.Count}",
                    index + 1,
                    questionsData.Count,
                    normalizedTopicTag);

                var question = new Question
                {
                    DocumentId = documentId,
                    QuestionText = q.QuestionText,
                    QuestionType = type,
                    CorrectAnswer = q.CorrectAnswer,
                    Explanation = q.Explanation,
                    Difficulty = ParseDifficulty(q.Difficulty),
                    Topic = normalizedTopicTag,
                    CreatedAt = DateTime.UtcNow
                };
                question.SetOptions(q.Options ?? new List<QuestionOption>());
                return question;
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating questions by type: {Type}", type);
            ReportProgress(progress, 100, "failed", $"Lỗi sinh câu hỏi: {ex.Message}");
            return CreateFallbackQuestions(documentId, count, type, topicTagCatalog: null);
        }
        finally
        {
            ReportProgress(progress, 100, "completed", "Hoàn tất sinh câu hỏi");
        }
    }

    private string GenerateMultipleChoicePrompt(string preparedContent, string topicGuidance, List<string> topicTagCatalog, List<string> keyPointsForQuestions, int count)
    {
        return $@"Based on the following full-document context, generate {count} multiple choice questions with 4 options each.

Content:
{preparedContent}

Topic analysis:
{topicGuidance}

Allowed TOPIC_TAG catalog (must choose exact value for each question.topic):
{BuildTopicTagPromptBlock(topicTagCatalog)}

Priority key points for question generation:
{BuildKeyPointsPromptBlock(keyPointsForQuestions)}

Write the questionText, option text, explanation, and topic in {OutputLanguage}. Keep the option keys as A, B, C, D.
Distribute the questions across the analyzed topics, and set topic as an exact tag from the allowed catalog.
At least 80% of questions should directly test one of the priority key points.

Respond in JSON array format with questionText, options (A,B,C,D), correctAnswer, explanation, difficulty, and topic.";
    }

    private string GenerateTrueFalsePrompt(string preparedContent, string topicGuidance, List<string> topicTagCatalog, List<string> keyPointsForQuestions, int count)
    {
        return $@"Based on the following full-document context, generate {count} True/False questions.

Content:
{preparedContent}

Topic analysis:
{topicGuidance}

Allowed TOPIC_TAG catalog (must choose exact value for each question.topic):
{BuildTopicTagPromptBlock(topicTagCatalog)}

Priority key points for question generation:
{BuildKeyPointsPromptBlock(keyPointsForQuestions)}

Write all statements, explanations, and topics in {OutputLanguage}. Use Vietnamese wording for the options.
Distribute the questions across the analyzed topics and different parts of the document.
Set topic as an exact tag from the allowed catalog.
At least 80% of questions should directly test one of the priority key points.

Respond in JSON array format:
[
  {{
    ""questionText"": ""Statement to evaluate"",
    ""options"": [
      {{""key"": ""A"", ""text"": ""Đúng"", ""isCorrect"": true}},
      {{""key"": ""B"", ""text"": ""Sai"", ""isCorrect"": false}}
    ],
    ""correctAnswer"": ""A"",
    ""explanation"": ""Why this is true/false"",
    ""difficulty"": ""Easy"",
    ""topic"": ""chu-de-chinh:y-nho-cu-the""
  }}
]";
    }

            private string GenerateShortAnswerPrompt(string preparedContent, string topicGuidance, List<string> topicTagCatalog, List<string> keyPointsForQuestions, int count)
    {
        return $@"Based on the following full-document context, generate {count} short answer questions.

Content:
{preparedContent}

Topic analysis:
{topicGuidance}

Allowed TOPIC_TAG catalog (must choose exact value for each question.topic):
{BuildTopicTagPromptBlock(topicTagCatalog)}

Priority key points for question generation:
{BuildKeyPointsPromptBlock(keyPointsForQuestions)}

Write all questions, answers, explanations, and topics in {OutputLanguage}.
Distribute the questions across the analyzed topics.
Set topic as an exact tag from the allowed catalog.
At least 80% of questions should directly test one of the priority key points.

Respond in JSON array format:
[
  {{
    ""questionText"": ""Question requiring short answer"",
    ""correctAnswer"": ""Expected answer"",
    ""explanation"": ""Detailed explanation"",
    ""difficulty"": ""Medium"",
    ""topic"": ""chu-de-chinh:y-nho-cu-the""
  }}
]";
    }

                private string GenerateFillInBlankPrompt(string preparedContent, string topicGuidance, List<string> topicTagCatalog, List<string> keyPointsForQuestions, int count)
    {
        return $@"Based on the following full-document context, generate {count} fill-in-the-blank questions.
Use _____ to indicate where the blank should be.

Content:
{preparedContent}

Topic analysis:
{topicGuidance}

Allowed TOPIC_TAG catalog (must choose exact value for each question.topic):
{BuildTopicTagPromptBlock(topicTagCatalog)}

Priority key points for question generation:
{BuildKeyPointsPromptBlock(keyPointsForQuestions)}

Write all questions, answers, explanations, and topics in {OutputLanguage}.
Distribute the questions across the analyzed topics.
Set topic as an exact tag from the allowed catalog.
At least 80% of questions should directly test one of the priority key points.

Respond in JSON array format:
[
  {{
    ""questionText"": ""Sentence with _____ for the blank"",
    ""correctAnswer"": ""Word or phrase that fills the blank"",
    ""explanation"": ""Context and explanation"",
    ""difficulty"": ""Medium"",
    ""topic"": ""chu-de-chinh:y-nho-cu-the""
  }}
]";
    }

    private async Task<string> BuildTopicGuidanceAsync(string preparedContent, int questionCount)
    {
        try
        {
            var systemPrompt = "You are an educational topic analyst. Extract precise topic structure and coverage guidance in Vietnamese for quiz generation.";
            var prompt = $@"Analyze the following full-document context and produce a compact topic blueprint for quiz generation.

Document context:
{preparedContent}

Requirements:
1. Extract 3-6 concrete main topics
2. For each topic, include 2-4 subtopics
3. Include a suggested number of questions per main topic for total {questionCount} questions
4. Include 6-12 critical facts that should appear in questions
5. Keep all text in Vietnamese and avoid generic labels
6. Main topics and subtopics must be concise noun phrases and should not overlap semantically

Return JSON only:
{{
  ""mainTopics"": [
    {{
      ""name"": ""Tên chủ đề chính"",
      ""subtopics"": [""ý nhỏ 1"", ""ý nhỏ 2""],
      ""targetQuestionCount"": 2
    }}
  ],
  ""criticalFacts"": [""fact 1"", ""fact 2""],
  ""coverageNote"": ""Ghi chú phân bổ câu hỏi""
}}";

            var blueprint = await _ollamaService.GenerateStructuredResponseAsync<TopicBlueprint>(prompt, systemPrompt);
            if (blueprint == null)
            {
                return BuildFallbackTopicGuidance(questionCount);
            }

            var validTopics = (blueprint.MainTopics ?? new List<TopicNode>())
                .Where(topic => !string.IsNullOrWhiteSpace(topic.Name))
                .Take(6)
                .Select(topic => new TopicNode
                {
                    Name = topic.Name.Trim(),
                    TargetQuestionCount = Math.Max(1, topic.TargetQuestionCount),
                    Subtopics = (topic.Subtopics ?? new List<string>())
                        .Where(subtopic => !string.IsNullOrWhiteSpace(subtopic))
                        .Take(4)
                        .ToList()
                })
                .ToList();

            if (!validTopics.Any())
            {
                return BuildFallbackTopicGuidance(questionCount);
            }

            var normalizedBlueprint = new TopicBlueprint
            {
                MainTopics = validTopics,
                CriticalFacts = (blueprint.CriticalFacts ?? new List<string>())
                    .Where(fact => !string.IsNullOrWhiteSpace(fact))
                    .Take(12)
                    .ToList(),
                CoverageNote = string.IsNullOrWhiteSpace(blueprint.CoverageNote)
                    ? $"Phân bổ đều cho {validTopics.Count} chủ đề chính."
                    : blueprint.CoverageNote
            };

            return JsonSerializer.Serialize(normalizedBlueprint, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error building topic guidance for question generation. Using fallback guidance.");
            return BuildFallbackTopicGuidance(questionCount);
        }
    }

    private static string BuildFallbackTopicGuidance(int questionCount)
    {
        var fallback = new TopicBlueprint
        {
            MainTopics = new List<TopicNode>
            {
                new()
                {
                    Name = "Nội dung trọng tâm",
                    Subtopics = new List<string> { "Khái niệm chính", "Ứng dụng" },
                    TargetQuestionCount = Math.Max(1, questionCount / 2)
                },
                new()
                {
                    Name = "Chi tiết mở rộng",
                    Subtopics = new List<string> { "So sánh", "Ví dụ minh họa" },
                    TargetQuestionCount = Math.Max(1, questionCount - Math.Max(1, questionCount / 2))
                }
            },
            CriticalFacts = new List<string>(),
            CoverageNote = "Phân bổ câu hỏi đều cho các chủ đề và tránh trùng lặp."
        };

        return JsonSerializer.Serialize(fallback, new JsonSerializerOptions { WriteIndented = true });
    }

    private List<Question> CreateFallbackQuestions(int documentId, int count, QuestionType questionType, List<string>? topicTagCatalog)
    {
        var questions = new List<Question>();
        var fallbackCount = Math.Min(Math.Max(1, count), 5);
        
        for (int i = 0; i < fallbackCount; i++)
        {
            var fallbackTopicTag = topicTagCatalog != null && topicTagCatalog.Any()
                ? NormalizeTopicTag(null, topicTagCatalog, i)
                : DefaultTopicTag;

            var question = new Question
            {
                DocumentId = documentId,
                QuestionText = BuildFallbackQuestionText(questionType, i + 1),
                QuestionType = questionType,
                CorrectAnswer = BuildFallbackCorrectAnswer(questionType),
                Explanation = "Day la cau hoi du phong khi AI chua tao duoc cau hoi hop le.",
                Difficulty = DifficultyLevel.Medium,
                Topic = fallbackTopicTag,
                CreatedAt = DateTime.UtcNow
            };

            var fallbackOptions = BuildFallbackOptions(questionType);
            if (fallbackOptions.Any())
            {
                question.SetOptions(fallbackOptions);
            }
            
            questions.Add(question);
        }
        
        return questions;
    }

    private static string BuildFallbackQuestionText(QuestionType questionType, int index)
    {
        return questionType switch
        {
            QuestionType.TrueFalse => $"Mau cau {index}: Menh de sau la dung hay sai?",
            QuestionType.ShortAnswer => $"Mau cau {index}: Tra loi ngan cho noi dung trong tai lieu.",
            QuestionType.FillInTheBlank => $"Mau cau {index}: Dien vao cho trong _____ theo noi dung tai lieu.",
            _ => $"Cau hoi mau {index} duoc tao tu noi dung tai lieu"
        };
    }

    private static string BuildFallbackCorrectAnswer(QuestionType questionType)
    {
        return questionType switch
        {
            QuestionType.ShortAnswer => "Cau tra loi mau",
            QuestionType.FillInTheBlank => "Tu/cum tu mau",
            _ => "A"
        };
    }

    private static List<QuestionOption> BuildFallbackOptions(QuestionType questionType)
    {
        return questionType switch
        {
            QuestionType.TrueFalse => new List<QuestionOption>
            {
                new() { Key = "A", Text = "Đúng", IsCorrect = true },
                new() { Key = "B", Text = "Sai", IsCorrect = false }
            },
            QuestionType.MultipleChoice => new List<QuestionOption>
            {
                new() { Key = "A", Text = "Lua chon A", IsCorrect = true },
                new() { Key = "B", Text = "Lua chon B", IsCorrect = false },
                new() { Key = "C", Text = "Lua chon C", IsCorrect = false },
                new() { Key = "D", Text = "Lua chon D", IsCorrect = false }
            },
            _ => new List<QuestionOption>()
        };
    }

    private async Task<string> BuildDocumentQuestioningContextAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalizedContent = content.Replace("\r\n", "\n").Trim();
        var allChunks = SplitIntoChunks(normalizedContent, ChunkSize, ChunkOverlap);

        if (allChunks.Count <= 1)
        {
            return normalizedContent;
        }

        var chunks = SelectRepresentativeChunks(allChunks, MaxChunksForQuestioningContext);

        _logger.LogInformation("Preparing full-document context from {SelectedChunkCount}/{TotalChunkCount} representative chunks for faster question generation", chunks.Count, allChunks.Count);

        var chunkSummaryTasks = chunks.Select((chunk, index) => SummarizeChunkForQuestionsAsync(chunk, index + 1, chunks.Count)).ToList();
        var chunkSummaries = (await Task.WhenAll(chunkSummaryTasks))
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .ToList();

        if (!chunkSummaries.Any())
        {
            return normalizedContent;
        }

        return $@"This context was prepared by reading the entire document in {chunks.Count} chunks.

{string.Join("\n\n", chunkSummaries)}";
    }

    private async Task<string> SummarizeChunkForQuestionsAsync(string chunk, int chunkNumber, int totalChunks)
    {
        try
        {
            _logger.LogInformation("Chunk summarization progress {Current}/{Total}: started", chunkNumber, totalChunks);
            var systemPrompt = "You are an educational analyst. Read the provided chunk carefully and extract concise, factual study notes in Vietnamese.";
            var prompt = $@"Read chunk {chunkNumber}/{totalChunks} of a document and extract concise notes for later quiz generation.

Chunk content:
{chunk}

Return valid JSON only:
{{
  ""topics"": [""specific topic 1"", ""specific topic 2""],
  ""keyFacts"": [""important fact 1"", ""important fact 2"", ""important fact 3""],
  ""sectionSummary"": ""2-4 sentence summary in Vietnamese""
}}";

            var result = await _ollamaService.GenerateStructuredResponseAsync<ChunkOutline>(prompt, systemPrompt);
            if (result == null)
            {
                _logger.LogWarning("Failed to summarize chunk {ChunkNumber}/{TotalChunks}; using raw excerpt fallback", chunkNumber, totalChunks);
                return $"[Chunk {chunkNumber}/{totalChunks}]\nTom tat tam thoi: {chunk.Substring(0, Math.Min(800, chunk.Length))}...";
            }

            var topics = result.Topics?.Where(topic => !string.IsNullOrWhiteSpace(topic)).Take(4).ToList() ?? new List<string>();
            var facts = result.KeyFacts?.Where(fact => !string.IsNullOrWhiteSpace(fact)).Take(6).ToList() ?? new List<string>();

            _logger.LogInformation("Chunk summarization progress {Current}/{Total}: completed with {TopicCount} topics and {FactCount} key facts", chunkNumber, totalChunks, topics.Count, facts.Count);

            return $@"[Chunk {chunkNumber}/{totalChunks}]
Topics: {string.Join(", ", topics)}
Summary: {result.SectionSummary}
Key facts:
- {string.Join("\n- ", facts)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error summarizing chunk {ChunkNumber}/{TotalChunks}; using raw excerpt fallback", chunkNumber, totalChunks);
            return $"[Chunk {chunkNumber}/{totalChunks}]\nTom tat tam thoi: {chunk.Substring(0, Math.Min(800, chunk.Length))}...";
        }
    }

    private static List<string> SelectRepresentativeChunks(List<string> chunks, int maxChunks)
    {
        if (chunks.Count <= maxChunks)
        {
            return chunks;
        }

        if (maxChunks <= 1)
        {
            return new List<string> { chunks[0] };
        }

        var selectedIndexes = Enumerable.Range(0, maxChunks)
            .Select(index => (int)Math.Round(index * (chunks.Count - 1d) / (maxChunks - 1d)))
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        return selectedIndexes.Select(index => chunks[index]).ToList();
    }

    private static List<string> SplitIntoChunks(string content, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < content.Length)
        {
            var length = Math.Min(chunkSize, content.Length - start);
            var end = start + length;

            if (end < content.Length)
            {
                var nextParagraph = content.LastIndexOf("\n\n", end, Math.Min(length, 800));
                if (nextParagraph > start + (chunkSize / 2))
                {
                    end = nextParagraph;
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

    private DifficultyLevel ParseDifficulty(string? difficulty)
    {
        return difficulty?.ToLowerInvariant() switch
        {
            "easy" => DifficultyLevel.Easy,
            "hard" => DifficultyLevel.Hard,
            _ => DifficultyLevel.Medium
        };
    }

    private static List<string> BuildTopicTagCatalog(string topicGuidance)
    {
        try
        {
            var blueprint = JsonSerializer.Deserialize<TopicBlueprint>(topicGuidance, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var tags = new List<string>();
            foreach (var topic in blueprint?.MainTopics ?? new List<TopicNode>())
            {
                if (string.IsNullOrWhiteSpace(topic.Name))
                {
                    continue;
                }

                var topicName = topic.Name.Trim();
                var subtopics = (topic.Subtopics ?? new List<string>())
                    .Where(subtopic => !string.IsNullOrWhiteSpace(subtopic))
                    .Select(subtopic => subtopic.Trim())
                    .Take(4)
                    .ToList();

                if (!subtopics.Any())
                {
                    subtopics.Add(topicName);
                }

                foreach (var subtopic in subtopics)
                {
                    var tag = CreateTopicTag(topicName, subtopic);
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        tags.Add(tag);
                    }
                }
            }

            var distinctTags = tags
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();

            return distinctTags.Any()
                ? distinctTags
                : new List<string> { "noi-dung-trong-tam:khai-niem-chinh", "chi-tiet-mo-rong:vi-du-minh-hoa" };
        }
        catch
        {
            return new List<string> { "noi-dung-trong-tam:khai-niem-chinh", "chi-tiet-mo-rong:vi-du-minh-hoa" };
        }
    }

    private static string BuildTopicTagPromptBlock(List<string> topicTagCatalog)
    {
        return string.Join("\n", topicTagCatalog.Select((tag, index) => $"{index + 1}. {tag}"));
    }

    private static List<string> ExtractCriticalFacts(string topicGuidance, int maxFacts)
    {
        try
        {
            var blueprint = JsonSerializer.Deserialize<TopicBlueprint>(topicGuidance, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var facts = (blueprint?.CriticalFacts ?? new List<string>())
                .Where(fact => !string.IsNullOrWhiteSpace(fact))
                .Select(fact => fact.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxFacts))
                .ToList();

            return facts;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string BuildKeyPointsPromptBlock(List<string> keyPointsForQuestions)
    {
        if (!keyPointsForQuestions.Any())
        {
            return "- Không có key points tường minh; ưu tiên các dữ kiện quan trọng trong topic analysis.";
        }

        return string.Join("\n", keyPointsForQuestions.Select((fact, index) => $"{index + 1}. {fact}"));
    }

    private static string NormalizeTopicTag(string? modelTopic, List<string> topicTagCatalog, int questionIndex)
    {
        if (!topicTagCatalog.Any())
        {
            return DefaultTopicTag;
        }

        if (string.IsNullOrWhiteSpace(modelTopic))
        {
            return topicTagCatalog[questionIndex % topicTagCatalog.Count];
        }

        var value = modelTopic.Trim();
        var exact = topicTagCatalog.FirstOrDefault(tag => string.Equals(tag, value, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        var normalizedModelTag = NormalizeTagToken(value);
        if (string.IsNullOrWhiteSpace(normalizedModelTag))
        {
            return topicTagCatalog[questionIndex % topicTagCatalog.Count];
        }

        var normalizedMatch = topicTagCatalog.FirstOrDefault(tag =>
            string.Equals(NormalizeTagToken(tag), normalizedModelTag, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(normalizedMatch))
        {
            return normalizedMatch;
        }

        var fuzzyMatch = topicTagCatalog.FirstOrDefault(tag =>
        {
            var normalizedCatalogTag = NormalizeTagToken(tag);
            return normalizedCatalogTag.Contains(normalizedModelTag, StringComparison.OrdinalIgnoreCase)
                || normalizedModelTag.Contains(normalizedCatalogTag, StringComparison.OrdinalIgnoreCase);
        });

        return !string.IsNullOrWhiteSpace(fuzzyMatch)
            ? fuzzyMatch
            : topicTagCatalog[questionIndex % topicTagCatalog.Count];
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
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (unicodeCategory == UnicodeCategory.NonSpacingMark)
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

    private static void ReportProgress(
        IProgress<QuestionGenerationProgressUpdate>? progress,
        int percent,
        string stage,
        string message,
        int? current = null,
        int? total = null,
        string? topicTag = null)
    {
        progress?.Report(new QuestionGenerationProgressUpdate
        {
            Percent = Math.Clamp(percent, 0, 100),
            Stage = stage,
            Message = message,
            Current = current,
            Total = total,
            TopicTag = topicTag
        });
    }

    private class QuestionData
    {
        public string QuestionText { get; set; } = string.Empty;
        public List<QuestionOption>? Options { get; set; }
        public string? CorrectAnswer { get; set; }
        public string? Explanation { get; set; }
        public string? Difficulty { get; set; }
        public string? Topic { get; set; }
    }

    private class ChunkOutline
    {
        public List<string>? Topics { get; set; }
        public List<string>? KeyFacts { get; set; }
        public string? SectionSummary { get; set; }
    }

    private class TopicBlueprint
    {
        public List<TopicNode>? MainTopics { get; set; }
        public List<string>? CriticalFacts { get; set; }
        public string? CoverageNote { get; set; }
    }

    private class TopicNode
    {
        public string Name { get; set; } = string.Empty;
        public List<string>? Subtopics { get; set; }
        public int TargetQuestionCount { get; set; }
    }
}
