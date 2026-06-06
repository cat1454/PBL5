using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class DocumentProcessingMetadataTests
{
    [Fact]
    public void ProcessingMetadata_RoundTripsExternalParsingFields()
    {
        var document = CreateDocument();
        document.SetProcessingMetadata(new DocumentProcessingMetadata
        {
            ExtractionProvider = "docling",
            ExternalParsingSucceeded = true,
            ExternalParsingElapsedMs = 1234,
            ExternalParsingError = null
        });

        var metadata = document.GetProcessingMetadata();

        Assert.Equal("docling", metadata.ExtractionProvider);
        Assert.True(metadata.ExternalParsingSucceeded);
        Assert.Equal(1234, metadata.ExternalParsingElapsedMs);
        Assert.Null(metadata.ExternalParsingError);
    }

    [Fact]
    public void ProcessingMetadata_DeserializesLegacyJsonWithoutExternalParsingFields()
    {
        var document = CreateDocument();
        document.ProcessedMetadataJson = """{"DocumentType":"REPORT","Language":"en"}""";

        var metadata = document.GetProcessingMetadata();

        Assert.Equal(DocumentTypes.Report, metadata.DocumentType);
        Assert.Equal("en", metadata.Language);
        Assert.Null(metadata.ExtractionProvider);
        Assert.Null(metadata.ExternalParsingSucceeded);
        Assert.Null(metadata.ExternalParsingElapsedMs);
        Assert.Null(metadata.ExternalParsingError);
    }

    private static Document CreateDocument()
        => new()
        {
            FileName = "document.pdf",
            FileType = "pdf",
            FilePath = "uploads/document.pdf",
            UploadedBy = "test-user"
        };
}
