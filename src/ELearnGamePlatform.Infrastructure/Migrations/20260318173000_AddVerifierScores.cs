using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    public partial class AddVerifierScores : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "verifier_score",
                table: "questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verifier_issues",
                table: "questions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "verifier_score",
                table: "slide_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verifier_issues",
                table: "slide_items",
                type: "jsonb",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "verifier_score",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "verifier_issues",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "verifier_score",
                table: "slide_items");

            migrationBuilder.DropColumn(
                name: "verifier_issues",
                table: "slide_items");
        }
    }
}
