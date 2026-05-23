using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Configuration;
using ELearnGamePlatform.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class AutoRepairEvidenceTests
{
    [Fact]
    public async Task EvidenceLogger_WritesJsonlAndCapsPreviewsAt500Characters()
    {
        var root = CreateTempDirectory();
        var logger = new FileAutoRepairEvidenceLogger(new TestEnvironment(root));
        var longPreview = new string('x', 650);

        await logger.LogAsync(new AutoRepairEvidenceRecord
        {
            CorrelationId = "corr-1",
            DocumentId = 7,
            Module = AutoRepairEvidenceModule.QuestionGeneration,
            Stage = AutoRepairEvidenceStage.RawOutputValidation,
            Model = "qwen-test",
            RawOutputValid = false,
            ErrorType = AutoRepairJsonErrorType.ParseError,
            ErrorMessage = "bad json",
            AutoRepairTriggered = true,
            RepairSuccess = true,
            FinalOutputValid = true,
            ElapsedMs = 123,
            RawOutputPreview = longPreview,
            RepairedOutputPreview = "fixed"
        });

        var logPath = Path.Combine(root, "logs", "auto-repair-evidence.jsonl");
        var line = Assert.Single(await File.ReadAllLinesAsync(logPath));
        using var document = JsonDocument.Parse(line);

        Assert.Equal("corr-1", document.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("QuestionGeneration", document.RootElement.GetProperty("module").GetString());
        Assert.Equal(500, document.RootElement.GetProperty("rawOutputPreview").GetString()!.Length);
        Assert.Equal("fixed", document.RootElement.GetProperty("repairedOutputPreview").GetString());
    }

    [Fact]
    public async Task OllamaStructuredGeneration_RepairsMalformedJsonAndReportsRealMetadata()
    {
        var handler = new QueueHttpHandler(
            """{"response":"{\"name\":\"raw\",}","done":true,"model":"qwen-test"}""",
            """{"response":"{\"name\":\"repaired\"}","done":true,"model":"qwen-test"}""");
        var client = new HttpClient(handler);
        var service = new OllamaService(
            client,
            Options.Create(new OllamaSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "qwen-test",
                GenerationModel = "qwen-test"
            }),
            NullLogger<OllamaService>.Instance);

        var result = await service.GenerateStructuredResponseWithMetadataAsync<TestPayload>(
            "Return JSON only:\n{\"name\":\"value\"}");

        Assert.Equal("repaired", result.Value?.Name);
        Assert.False(result.RawOutputValid);
        Assert.True(result.AutoRepairTriggered);
        Assert.True(result.RepairSuccess);
        Assert.True(result.FinalOutputValid);
        Assert.Equal(AutoRepairJsonErrorType.ParseError, result.ErrorType);
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains("raw", result.RawOutputPreview);
        Assert.Contains("repaired", result.RepairedOutputPreview);
    }

    [Fact]
    public async Task OllamaStructuredGeneration_DeterministicallyRepairsCommonSlideWrongTypes()
    {
        var handler = new QueueHttpHandler(
            """{"response":"{\"bodyBlocks\":[{\"text\":\"Ý chính một\"},[\"Ý phụ\",\"Ý nối\"]],\"evidenceFromText\":{\"text\":\"Căn cứ từ tài liệu\"}}","done":true,"model":"qwen-test"}""");
        var client = new HttpClient(handler);
        var service = new OllamaService(
            client,
            Options.Create(new OllamaSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "qwen-test",
                GenerationModel = "qwen-test"
            }),
            NullLogger<OllamaService>.Instance);

        var result = await service.GenerateStructuredResponseWithMetadataAsync<TestSlidePayload>(
            "Return JSON only:\n{\"bodyBlocks\":[\"text\"],\"evidenceFromText\":\"text\"}");

        Assert.NotNull(result.Value);
        Assert.Equal(new[] { "Ý chính một", "Ý phụ; Ý nối" }, result.Value!.BodyBlocks);
        Assert.Equal("Căn cứ từ tài liệu", result.Value.EvidenceFromText);
        Assert.False(result.RawOutputValid);
        Assert.True(result.AutoRepairTriggered);
        Assert.True(result.RepairSuccess);
        Assert.True(result.FinalOutputValid);
        Assert.Equal(AutoRepairJsonErrorType.WrongType, result.ErrorType);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("bodyBlocks", result.RepairedOutputPreview);
    }

    [Fact]
    public async Task OllamaStructuredGeneration_DeterministicallyEscapesRawNewlinesInsideJsonStrings()
    {
        var malformedJson = "{\"name\":\"line one\nline two\"}";
        var handler = new QueueHttpHandler(JsonSerializer.Serialize(new
        {
            response = malformedJson,
            done = true,
            model = "qwen-test"
        }));
        var client = new HttpClient(handler);
        var service = new OllamaService(
            client,
            Options.Create(new OllamaSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "qwen-test",
                GenerationModel = "qwen-test"
            }),
            NullLogger<OllamaService>.Instance);

        var result = await service.GenerateStructuredResponseWithMetadataAsync<TestPayload>(
            "Return JSON only:\n{\"name\":\"value\"}");

        Assert.Equal("line one\nline two", result.Value?.Name);
        Assert.False(result.RawOutputValid);
        Assert.True(result.AutoRepairTriggered);
        Assert.True(result.RepairSuccess);
        Assert.True(result.FinalOutputValid);
        Assert.Equal(AutoRepairJsonErrorType.ParseError, result.ErrorType);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("\\n", result.RepairedOutputPreview);
    }

    [Fact]
    public async Task ReportScript_GeneratesBeforeAfterReductionTableFromJsonl()
    {
        var root = CreateTempDirectory();
        var inputPath = Path.Combine(root, "auto-repair-evidence.jsonl");
        var outputPath = Path.Combine(root, "auto-repair-log-report.md");
        var records = new[]
        {
            new AutoRepairEvidenceRecord
            {
                CorrelationId = "a",
                DocumentId = 1,
                Module = AutoRepairEvidenceModule.QuestionGeneration,
                Stage = AutoRepairEvidenceStage.RawOutputValidation,
                Model = "qwen",
                RawOutputValid = false,
                ErrorType = AutoRepairJsonErrorType.ParseError,
                ErrorMessage = "bad",
                AutoRepairTriggered = true,
                RepairSuccess = true,
                FinalOutputValid = true,
                ElapsedMs = 10,
                RawOutputPreview = "{bad",
                RepairedOutputPreview = "{}"
            },
            new AutoRepairEvidenceRecord
            {
                CorrelationId = "b",
                DocumentId = 1,
                Module = AutoRepairEvidenceModule.SlideGeneration,
                Stage = AutoRepairEvidenceStage.RawOutputValidation,
                Model = "qwen",
                RawOutputValid = false,
                ErrorType = AutoRepairJsonErrorType.WrongType,
                ErrorMessage = "wrong",
                AutoRepairTriggered = true,
                RepairSuccess = false,
                FinalOutputValid = false,
                ElapsedMs = 10,
                RawOutputPreview = "[]",
                RepairedOutputPreview = "[]"
            },
            new AutoRepairEvidenceRecord
            {
                CorrelationId = "c",
                DocumentId = 2,
                Module = AutoRepairEvidenceModule.QuestionGeneration,
                Stage = AutoRepairEvidenceStage.RawOutputValidation,
                Model = "qwen",
                RawOutputValid = true,
                ErrorType = AutoRepairJsonErrorType.None,
                ErrorMessage = "",
                AutoRepairTriggered = false,
                RepairSuccess = false,
                FinalOutputValid = true,
                ElapsedMs = 10,
                RawOutputPreview = "{}",
                RepairedOutputPreview = ""
            }
        };

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        await File.WriteAllLinesAsync(inputPath, records.Select(record => JsonSerializer.Serialize(record, jsonOptions)));

        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "generate-auto-repair-report.ps1"));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-InputPath");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-OutputPath");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        Assert.True(process.ExitCode == 0, stderr);

        var report = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("| Total AI outputs tested | 3 |", report);
        Assert.Contains("| Invalid raw JSON outputs | 2 |", report);
        Assert.Contains("| Outputs still invalid after Auto-repair | 1 |", report);
        Assert.Contains("| Absolute reduction | 33.33 percentage points |", report);
        Assert.Contains("| Relative reduction | 50.00% |", report);
        Assert.Contains("| ParseError | 1 |", report);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "elearn-auto-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestPayload
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestSlidePayload
    {
        public List<string> BodyBlocks { get; set; } = new();
        public string EvidenceFromText { get; set; } = string.Empty;
    }

    private sealed class QueueHttpHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueHttpHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount += 1;
            var response = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
            WebRootFileProvider = ContentRootFileProvider;
        }

        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
