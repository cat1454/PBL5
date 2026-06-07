using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePrimaryKeyColumnNames : Migration
    {
        private static readonly string[] Tables =
        {
            "slide_items",
            "slide_decks",
            "questions",
            "learning_test_results",
            "learning_progresses",
            "learning_attempts",
            "game_sessions",
            "folder_projects",
            "documents",
            "document_understanding_runs",
            "app_users"
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.RenameColumn(
                    name: "Id",
                    table: table,
                    newName: "id");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.RenameColumn(
                    name: "id",
                    table: table,
                    newName: "Id");
            }
        }
    }
}
