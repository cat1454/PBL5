using ELearnGamePlatform.Core.Entities;
using Microsoft.AspNetCore.Http;

namespace ELearnGamePlatform.API.Services;

public interface IDocumentIngestionService
{
    Task<Document> UploadDocumentAsync(IFormFile file, string userId, int? folderProjectId = null);
    void StartBackgroundProcessing(int documentId);
}
