using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.PostgresTypes;

namespace ELearnGamePlatform.Infrastructure.Repositories;

public class DocumentUnderstandingRunRepository : IDocumentUnderstandingRunRepository
{
    private readonly ApplicationDbContext _context;

    public DocumentUnderstandingRunRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DocumentUnderstandingRun> CreateAsync(DocumentUnderstandingRun run)
    {
        _context.DocumentUnderstandingRuns.Add(run);
        await _context.SaveChangesAsync();
        return run;
    }

    public async Task<DocumentUnderstandingRun?> GetLatestByDocumentIdAsync(int documentId)
    {
        try
        {
            return await _context.DocumentUnderstandingRuns
                .Where(run => run.DocumentId == documentId)
                .OrderByDescending(run => run.CreatedAt)
                .ThenByDescending(run => run.Id)
                .FirstOrDefaultAsync();
        }
        catch (PostgresException ex) when (IsDocumentUnderstandingTableMissing(ex))
        {
            return null;
        }
    }

    private static bool IsDocumentUnderstandingTableMissing(PostgresException ex)
        => ex.SqlState == PostgresErrorCodes.UndefinedTable
            && (string.IsNullOrWhiteSpace(ex.TableName)
                || string.Equals(ex.TableName, "document_understanding_runs", StringComparison.OrdinalIgnoreCase)
                || ex.MessageText.Contains("document_understanding_runs", StringComparison.OrdinalIgnoreCase));
}
