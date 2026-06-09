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

    [Fact]
    public void TempQueryDb()
    {
        var connString = "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=123qwe!@#;SslMode=disable";
        using var conn = new Npgsql.NpgsqlConnection(connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT d.""Id"", d.""FileName"", d.""Status"", d.""FolderProjectId"", d.""ExtractedText"", d.""processed_metadata"" 
            FROM ""Documents"" d 
            ORDER BY d.""Id"" DESC LIMIT 30";
        using var reader = cmd.ExecuteReader();
        var sb = new System.Text.StringBuilder();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var name = reader.GetString(1);
            var status = reader.GetInt32(2);
            var folderId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
            var textLength = reader.IsDBNull(4) ? 0 : reader.GetString(4).Length;
            var metadata = reader.IsDBNull(5) ? "" : reader.GetString(5);
            sb.AppendLine($"ID: {id}, Name: {name}, Status: {status}, FolderId: {folderId}, TextLength: {textLength}, Metadata: {metadata}");
        }
        throw new System.Exception(sb.ToString());
    }
}

