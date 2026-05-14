using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260514145500_RepairMissingDocumentOcrReviewColumns")]
    public partial class RepairMissingDocumentOcrReviewColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                ADD COLUMN IF NOT EXISTS raw_ocr_text text;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                ADD COLUMN IF NOT EXISTS cleaned_text text;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                ADD COLUMN IF NOT EXISTS is_text_reviewed boolean NOT NULL DEFAULT false;
            """);

            migrationBuilder.Sql("""
                UPDATE public.documents
                SET raw_ocr_text = extracted_text
                WHERE raw_ocr_text IS NULL
                  AND extracted_text IS NOT NULL;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                DROP COLUMN IF EXISTS is_text_reviewed;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                DROP COLUMN IF EXISTS cleaned_text;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                DROP COLUMN IF EXISTS raw_ocr_text;
            """);
        }
    }
}
