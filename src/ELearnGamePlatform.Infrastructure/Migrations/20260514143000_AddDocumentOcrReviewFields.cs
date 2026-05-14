using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentOcrReviewFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "raw_ocr_text",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cleaned_text",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_text_reviewed",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE documents SET raw_ocr_text = extracted_text WHERE raw_ocr_text IS NULL AND extracted_text IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "raw_ocr_text",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "cleaned_text",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "is_text_reviewed",
                table: "documents");
        }
    }
}
