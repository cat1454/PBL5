using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Services.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class LocalFirstPipelineTests
{
    private readonly TokenEstimator _tokenEstimator = new();

    [Fact]
    public void Chunker_KeepsChunksUnderMaxTokensAndRetainsHeading()
    {
        var text = "Chuong 1 Tong quan\n\n" + string.Join("\n\n", Enumerable.Range(1, 28).Select(index =>
            $"Khái niệm dữ liệu phần {index}. Ví dụ này giải thích nguyên nhân, kết quả và ứng dụng trong hệ thống học tập."));

        var chunks = DocumentStructureChunker.SplitIntoStructuredTokenChunks(text, 140, 220, 10, _tokenEstimator);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(_tokenEstimator.EstimateTokens(chunk.Text) <= 220));
        Assert.All(chunks, chunk => Assert.StartsWith("Chuong 1 Tong quan", chunk.Text));
    }

    [Fact]
    public void Chunker_UsesSemanticBoundaryReasonForKeywordSentences()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 16).Select(index =>
            $"Đây là đoạn học liệu số {index}. Nguyên nhân của hiện tượng được mô tả bằng dữ kiện cụ thể. Kết quả là người học hiểu quy trình và ví dụ áp dụng."));

        var chunks = DocumentStructureChunker.SplitIntoStructuredTokenChunks(text, 65, 100, 8, _tokenEstimator);

        Assert.Contains(chunks, chunk => chunk.ChunkingReason == "keyword-sentence-boundary");
    }

    [Fact]
    public void CoverageMap_ExtractsVietnameseKeywordsAnchorsAndFacts()
    {
        var settings = TestSettings();
        var text = """
        1. Khái niệm quang hợp

        Quang hợp là quá trình cây xanh sử dụng ánh sáng mặt trời để tạo glucose. Năm 2024, thí nghiệm minh họa cho thấy cường độ ánh sáng ảnh hưởng đến tốc độ quang hợp. Ví dụ lá cây hấp thụ CO2 và giải phóng O2.
        """;

        var map = DocumentCoverageMapBuilder.Build(text, settings, _tokenEstimator);

        var chunk = Assert.Single(map);
        Assert.Contains(chunk.Keywords, keyword => Normalize(keyword).Contains("quang hop"));
        Assert.NotEmpty(chunk.ConceptAnchors);
        Assert.Contains(chunk.KeyFacts, fact => fact.Contains("2024", StringComparison.OrdinalIgnoreCase) || Normalize(fact).Contains("quang hop"));
        Assert.DoesNotContain(chunk.Keywords, keyword => string.Equals(Normalize(keyword), "la", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoverageMap_DowngradesFrontMatterAndOcrGarbage()
    {
        var settings = TestSettings();
        settings.TargetChunkTokens = 40;
        settings.MaxChunkTokens = 70;
        var text = """
        MỤC LỤC
        Chương 1 ........................................ 1
        Chương 2 ........................................ 9

        @@@ ### ||| 0O0O0O llll //// ???? *** @@@

        1. Nội dung chính
        Khái niệm hệ sinh thái là tập hợp quần xã sinh vật và môi trường sống. Ví dụ ao hồ có cá, rong, vi sinh vật và các yếu tố ánh sáng, nhiệt độ.
        """;

        var map = DocumentCoverageMapBuilder.Build(text, settings, _tokenEstimator);
        var garbageMap = DocumentCoverageMapBuilder.Build("@@@ ### ||| 0O0O0O llll //// ???? *** @@@", settings, _tokenEstimator);
        var cleanMap = DocumentCoverageMapBuilder.Build("1. Nội dung chính\n\nKhái niệm hệ sinh thái là tập hợp quần xã sinh vật và môi trường sống. Ví dụ ao hồ có cá, rong, vi sinh vật và các yếu tố ánh sáng, nhiệt độ.", settings, _tokenEstimator);

        Assert.Contains(map, chunk => chunk.NegativeSignals.Contains("front-matter-or-toc") || chunk.ChunkQualityScore < 35);
        Assert.True(cleanMap.Max(chunk => chunk.ChunkQualityScore) > garbageMap.Max(chunk => chunk.ChunkQualityScore));
    }

    [Fact]
    public async Task ContentAnalyzer_ProducesLocalProcessedContentWithoutOllama()
    {
        var ollama = new CapturingOllamaService { ThrowOnStructured = true };
        var analyzer = CreateAnalyzer(ollama, TestSettings(enableRefine: true));
        var text = LongEvidenceText();

        var result = await analyzer.AnalyzeContentAsync(text);

        Assert.NotEmpty(result.CoverageMap);
        Assert.NotEmpty(result.MainTopics);
        Assert.NotEmpty(result.KeyPoints);
        Assert.Contains(result.KeyPoints, point => Normalize(point).Contains("quang hop"));
    }

    [Fact]
    public async Task ContentAnalyzer_RefinePromptUsesCompactEvidenceOnly()
    {
        var rawNeedle = "RAW_FULL_CHUNK_TEXT_SENTINEL";
        var ollama = new CapturingOllamaService();
        var analyzer = CreateAnalyzer(ollama, TestSettings(enableRefine: true));
        var text = LongEvidenceText(rawNeedle);

        await analyzer.AnalyzeContentAsync(text);

        var refinePrompt = Assert.Single(ollama.StructuredPrompts.Where(prompt => prompt.Contains("Compact evidence:", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("keywords:", refinePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<<<", refinePrompt);
    }

    [Fact]
    public async Task ContentAnalyzer_WhenUnderstandingEnabled_UsesKnowledgeMapVisionContext()
    {
        var ollama = new CapturingOllamaService();
        var analyzer = CreateAnalyzer(
            ollama,
            TestSettings(enableRefine: true),
            new DocumentUnderstandingOptions { Enabled = true });
        var understanding = new DocumentUnderstandingResult
        {
            DocumentId = 21,
            Status = DocumentQualityStatuses.AutoGenerateAllowed,
            Confidence = 0.91d,
            CombinedText = LongEvidenceText("VISUALSENTINEL diagram source"),
            Pages =
            [
                new PageUnderstandingResult
                {
                    PageNumber = 1,
                    Confidence = 0.91d,
                    Text = LongEvidenceText("page evidence")
                }
            ],
            Regions =
            [
                new DocumentRegion
                {
                    PageNumber = 1,
                    RegionType = DocumentRegionTypes.DiagramCandidate,
                    Text = "ATP -> energy transfer",
                    Description = "Diagram shows ATP flow from sunlight capture to glucose production.",
                    ExtractedLabels = ["ATP", "glucose"],
                    Relationships = ["sunlight -> ATP -> glucose"],
                    VisionConfidence = 0.88d
                }
            ]
        };

        var result = await analyzer.AnalyzeContentAsync("legacy text without the visual sentinel", understanding);

        Assert.Contains(result.CoverageMap, chunk => (chunk.Text ?? string.Empty).Contains("VISUALSENTINEL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContentAnalyzer_WhenKnowledgeMapBuilderFails_FallsBackToLegacyText()
    {
        var ollama = new CapturingOllamaService { ThrowOnStructured = true };
        var analyzer = CreateAnalyzer(
            ollama,
            TestSettings(enableRefine: false),
            new DocumentUnderstandingOptions { Enabled = true },
            new ThrowingKnowledgeMapBuilder());
        var legacyText = LongEvidenceText("LEGACY_FALLBACK_SENTINEL");

        var result = await analyzer.AnalyzeContentAsync(
            legacyText,
            new DocumentUnderstandingResult
            {
                DocumentId = 22,
                Status = DocumentQualityStatuses.AutoGenerateAllowed,
                Confidence = 0.9d
            });

        Assert.Contains(result.CoverageMap, chunk => (chunk.Text ?? string.Empty).Contains("LEGACY_FALLBACK_SENTINEL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QuestionGenerator_UsesCompactEvidenceAndPreferredChunkIds()
    {
        var ollama = new CapturingOllamaService();
        var service = new QuestionGeneratorService(
            ollama,
            _tokenEstimator,
            Options.Create(TestSettings()),
            NullLogger<QuestionGeneratorService>.Instance);
        var processed = new ProcessedContent
        {
            MainTopics = new List<string> { "Quang hợp" },
            KeyPoints = new List<string> { "Quang hợp tạo glucose" },
            CoverageMap = new List<DocumentCoverageChunk>
            {
                CoverageChunk("C01", "Hô hấp", "hô hấp tế bào", 70),
                CoverageChunk("C02", "Quang hợp", "quang hợp glucose ánh sáng", 90),
                CoverageChunk("C03", "Lịch sử", "thông tin chung", 35)
            }
        };

        await service.GenerateQuestionsAsync(7, LongEvidenceText(), 1, processed);

        var generationPrompt = ollama.StructuredPrompts.First(prompt => prompt.Contains("Evidence library:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("[C02]", generationPrompt);
        Assert.Contains("Keywords:", generationPrompt);
        Assert.DoesNotContain("Text:\r\n<<<", generationPrompt);
        Assert.DoesNotContain("Text:\n<<<", generationPrompt);
    }

    private ContentAnalyzerService CreateAnalyzer(
        IOllamaService ollama,
        LocalLlmSettings settings,
        DocumentUnderstandingOptions? understandingOptions = null,
        IDocumentKnowledgeMapBuilder? knowledgeMapBuilder = null)
    {
        var options = Options.Create(settings);
        var planner = new TokenBudgetPlanner(_tokenEstimator, options);
        var assembler = new PromptAssembler(_tokenEstimator, options);
        return new ContentAnalyzerService(
            ollama,
            planner,
            assembler,
            _tokenEstimator,
            options,
            Options.Create(understandingOptions ?? new DocumentUnderstandingOptions()),
            knowledgeMapBuilder ?? new DocumentKnowledgeMapBuilder(
                _tokenEstimator,
                options,
                NullLogger<DocumentKnowledgeMapBuilder>.Instance),
            NullLogger<ContentAnalyzerService>.Instance);
    }

    private static LocalLlmSettings TestSettings(bool enableRefine = false)
        => new()
        {
            TargetChunkTokens = 80,
            MaxChunkTokens = 140,
            ChunkOverlapTokens = 10,
            IncludeFullSelectedChunkText = false,
            EnableAnalysisRefine = enableRefine,
            MinTextLengthForAIRefine = 100,
            MinCoverageChunksForAIRefine = 1
        };

    private static string LongEvidenceText(string extra = "")
        => string.Join("\n\n", Enumerable.Range(1, 18).Select(index =>
            $"1.{index} Quang hợp\nQuang hợp là quá trình cây xanh dùng ánh sáng để tạo glucose và oxy. Nguyên nhân tốc độ quang hợp thay đổi là cường độ ánh sáng, nước và CO2. Kết quả thí nghiệm năm 2024 cho thấy lá cây giải phóng O2 nhiều hơn khi ánh sáng tăng. {extra}"));

    private static DocumentCoverageChunk CoverageChunk(string id, string heading, string fact, int quality)
        => new()
        {
            ChunkId = id,
            ChunkNumber = int.Parse(id[1..]),
            Zone = "giua",
            CoverageZone = "giua",
            Label = heading,
            HeadingPath = heading,
            Summary = fact,
            EvidenceExcerpt = fact,
            Keywords = fact.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4).ToList(),
            ConceptAnchors = new List<string> { heading },
            KeyFacts = new List<string> { fact },
            ChunkQualityScore = quality,
            TeachabilityScore = quality,
            IsEligibleForQuestionGeneration = true,
            TextTokenCount = 40,
            EstimatedTokenCount = 40
        };

    private static string Normalize(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    private sealed class CapturingOllamaService : IOllamaService
    {
        public List<string> StructuredPrompts { get; } = new();
        public bool ThrowOnStructured { get; init; }

        public Task<string> GenerateResponseAsync(string prompt, string? systemPrompt = null, OllamaModelProfile profile = OllamaModelProfile.Generation)
            => Task.FromResult(string.Empty);

        public Task<T?> GenerateStructuredResponseAsync<T>(string prompt, string? systemPrompt = null, OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class
        {
            StructuredPrompts.Add(prompt);
            if (ThrowOnStructured)
            {
                throw new HttpRequestException("Ollama unavailable in test");
            }

            return Task.FromResult<T?>(null);
        }

        public async Task<StructuredGenerationResult<T>> GenerateStructuredResponseWithMetadataAsync<T>(string prompt, string? systemPrompt = null, OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class
        {
            var value = await GenerateStructuredResponseAsync<T>(prompt, systemPrompt, profile);
            return new StructuredGenerationResult<T>
            {
                Value = value,
                Model = "test-model",
                RawOutputValid = value != null,
                ErrorType = value == null ? AutoRepairJsonErrorType.EmptyOutput : AutoRepairJsonErrorType.None,
                ErrorMessage = value == null ? "empty test response" : string.Empty,
                AutoRepairTriggered = false,
                RepairSuccess = false,
                FinalOutputValid = value != null,
                ElapsedMs = 0,
                RawOutputPreview = string.Empty,
                RepairedOutputPreview = string.Empty
            };
        }

        public Task<bool> IsAvailableAsync()
            => Task.FromResult(!ThrowOnStructured);
    }

    private sealed class ThrowingKnowledgeMapBuilder : IDocumentKnowledgeMapBuilder
    {
        public KnowledgeMapBuildResult Build(DocumentUnderstandingResult? result)
            => throw new InvalidOperationException("test builder failure");

        public KnowledgeMapBuildResult Build(DocumentUnderstandingRun? run)
            => throw new InvalidOperationException("test builder failure");
    }
}
