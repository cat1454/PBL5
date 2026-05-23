using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ELearnGamePlatform.Core.Interfaces;

namespace ELearnGamePlatform.API.Services;

public sealed class FileAutoRepairEvidenceLogger : IAutoRepairEvidenceLogger
{
    private const int PreviewLimit = 500;
    private readonly string _logPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileAutoRepairEvidenceLogger(IWebHostEnvironment environment)
    {
        _logPath = Path.Combine(environment.ContentRootPath, "logs", "auto-repair-evidence.jsonl");
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task LogAsync(AutoRepairEvidenceRecord record, CancellationToken cancellationToken = default)
    {
        var safeRecord = new AutoRepairEvidenceRecord
        {
            Timestamp = record.Timestamp,
            CorrelationId = record.CorrelationId,
            DocumentId = record.DocumentId,
            Module = record.Module,
            Stage = record.Stage,
            Model = record.Model,
            RawOutputValid = record.RawOutputValid,
            ErrorType = record.ErrorType,
            RawOutputPreview = NormalizePreview(record.RawOutputPreview),
            RepairedOutputPreview = NormalizePreview(record.RepairedOutputPreview),
            ErrorMessage = NormalizePreview(record.ErrorMessage),
            AutoRepairTriggered = record.AutoRepairTriggered,
            RepairSuccess = record.RepairSuccess,
            FinalOutputValid = record.FinalOutputValid,
            ElapsedMs = record.ElapsedMs
        };

        var directory = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = JsonSerializer.Serialize(safeRecord, _jsonOptions);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_logPath, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string NormalizePreview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        normalized = normalized.Trim();
        return normalized.Length <= PreviewLimit
            ? normalized
            : normalized[..PreviewLimit];
    }
}
