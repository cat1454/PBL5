namespace ELearnGamePlatform.Core.Interfaces;

public interface IOllamaService
{
    Task<string> GenerateResponseAsync(
        string prompt,
        string? systemPrompt = null,
        OllamaModelProfile profile = OllamaModelProfile.Generation);

    Task<T?> GenerateStructuredResponseAsync<T>(
        string prompt,
        string? systemPrompt = null,
        OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class;

    Task<StructuredGenerationResult<T>> GenerateStructuredResponseWithMetadataAsync<T>(
        string prompt,
        string? systemPrompt = null,
        OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class;

    Task<bool> IsAvailableAsync();
}

public enum OllamaModelProfile
{
    Analysis,
    Generation,
    Verification
}

public enum AutoRepairEvidenceModule
{
    QuestionGeneration,
    SlideGeneration
}

public enum AutoRepairEvidenceStage
{
    RawOutputValidation,
    AutoRepair,
    FinalValidation
}

public enum AutoRepairJsonErrorType
{
    ParseError,
    MissingField,
    WrongType,
    SchemaMismatch,
    EmptyOutput,
    None
}

public sealed class StructuredGenerationResult<T> where T : class
{
    public T? Value { get; init; }
    public string Model { get; init; } = string.Empty;
    public bool RawOutputValid { get; init; }
    public AutoRepairJsonErrorType ErrorType { get; init; } = AutoRepairJsonErrorType.None;
    public string ErrorMessage { get; init; } = string.Empty;
    public bool AutoRepairTriggered { get; init; }
    public bool RepairSuccess { get; init; }
    public bool FinalOutputValid { get; init; }
    public long ElapsedMs { get; init; }
    public string RawOutputPreview { get; init; } = string.Empty;
    public string RepairedOutputPreview { get; init; } = string.Empty;
}

public sealed class AutoRepairEvidenceRecord
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; init; } = string.Empty;
    public int? DocumentId { get; init; }
    public AutoRepairEvidenceModule Module { get; init; }
    public AutoRepairEvidenceStage Stage { get; init; }
    public string Model { get; init; } = string.Empty;
    public bool RawOutputValid { get; init; }
    public AutoRepairJsonErrorType ErrorType { get; init; } = AutoRepairJsonErrorType.None;
    public string ErrorMessage { get; init; } = string.Empty;
    public bool AutoRepairTriggered { get; init; }
    public bool RepairSuccess { get; init; }
    public bool FinalOutputValid { get; init; }
    public long ElapsedMs { get; init; }
    public string RawOutputPreview { get; init; } = string.Empty;
    public string RepairedOutputPreview { get; init; } = string.Empty;
}

public interface IAutoRepairEvidenceLogger
{
    Task LogAsync(AutoRepairEvidenceRecord record, CancellationToken cancellationToken = default);
}
