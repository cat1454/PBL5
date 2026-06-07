using ELearnGamePlatform.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class PrimaryKeyMigrationTests
{
    private static readonly string[] ExpectedTables =
    {
        "app_users",
        "document_understanding_runs",
        "documents",
        "folder_projects",
        "game_sessions",
        "learning_attempts",
        "learning_progresses",
        "learning_test_results",
        "questions",
        "slide_decks",
        "slide_items"
    };

    [Fact]
    public void NormalizePrimaryKeyColumnNames_OnlyRenamesExpectedColumns()
    {
        var migration = new NormalizePrimaryKeyColumnNames();

        AssertRenameOperations(migration.UpOperations, "Id", "id");
        AssertRenameOperations(migration.DownOperations, "id", "Id");
    }

    private static void AssertRenameOperations(
        IReadOnlyList<MigrationOperation> operations,
        string expectedName,
        string expectedNewName)
    {
        Assert.Equal(ExpectedTables.Length, operations.Count);
        Assert.All(operations, operation => Assert.IsType<RenameColumnOperation>(operation));

        var renames = operations.Cast<RenameColumnOperation>().ToList();

        Assert.Equal(ExpectedTables, renames.Select(operation => operation.Table).OrderBy(table => table));
        Assert.All(renames, operation =>
        {
            Assert.Equal(expectedName, operation.Name);
            Assert.Equal(expectedNewName, operation.NewName);
        });
    }
}
