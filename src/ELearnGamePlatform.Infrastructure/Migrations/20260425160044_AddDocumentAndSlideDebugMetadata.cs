using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentAndSlideDebugMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                ADD COLUMN IF NOT EXISTS evidence_debug text;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                ADD COLUMN IF NOT EXISTS processed_metadata text;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                DROP COLUMN IF EXISTS evidence_debug;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                DROP COLUMN IF EXISTS processed_metadata;
            """);
        }
    }
}
