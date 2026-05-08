namespace ELearnGamePlatform.Core.Entities;

public static class DocumentTypes
{
    public const string Textbook = "TEXTBOOK";
    public const string LectureNote = "LECTURE_NOTE";
    public const string ResearchPaper = "RESEARCH_PAPER";
    public const string Report = "REPORT";
    public const string Manual = "MANUAL";
    public const string Unknown = "UNKNOWN";
}

public static class ChunkClassifications
{
    public const string FrontMatter = "FRONT_MATTER";
    public const string Preface = "PREFACE";
    public const string TableOfContents = "TABLE_OF_CONTENTS";
    public const string LessonContent = "LESSON_CONTENT";
    public const string Example = "EXAMPLE";
    public const string Exercise = "EXERCISE";
    public const string Reference = "REFERENCE";
    public const string Appendix = "APPENDIX";
    public const string Noise = "NOISE";
}

public class DocumentProcessingMetadata
{
    public string DocumentType { get; set; } = DocumentTypes.Unknown;
    public string? Language { get; set; }
    public string? Title { get; set; }
    public int? MainContentStartPage { get; set; }
    public List<DocumentSectionDescriptor> Structure { get; set; } = new();
    public List<ExcludedContentDescriptor> ExcludedContent { get; set; } = new();
    public DocumentInputQualityResult? InputQuality { get; set; }
    public DocumentInputQualityReport? PageQualityReport { get; set; }
    public TokenBudgetPlan? AnalysisTokenBudget { get; set; }
}

public class DocumentSectionDescriptor
{
    public string SectionKey { get; set; } = string.Empty;
    public string? Heading { get; set; }
    public string Classification { get; set; } = ChunkClassifications.LessonContent;
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public List<string> ChunkIds { get; set; } = new();
}

public class ExcludedContentDescriptor
{
    public string? ChunkId { get; set; }
    public int? Page { get; set; }
    public string Classification { get; set; } = ChunkClassifications.Noise;
    public string Reason { get; set; } = string.Empty;
}

public class SlideEvidenceDebugMetadata
{
    public List<SlideEvidenceDebugChunk> SelectedChunks { get; set; } = new();
}

public class SlideEvidenceDebugChunk
{
    public string ChunkId { get; set; } = string.Empty;
    public string Classification { get; set; } = ChunkClassifications.LessonContent;
    public int TeachabilityScore { get; set; }
    public string ReasonSelected { get; set; } = string.Empty;
}
