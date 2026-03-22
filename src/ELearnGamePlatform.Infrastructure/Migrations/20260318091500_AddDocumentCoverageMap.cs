using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    public partial class AddDocumentCoverageMap : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "coverage_map",
                table: "documents",
                type: "jsonb",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "coverage_map",
                table: "documents");
        }
    }
}
