using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    public partial class AddSlideGroundingFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "evidence_from_text",
                table: "slide_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "key_message",
                table: "slide_items",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "evidence_from_text",
                table: "slide_items");

            migrationBuilder.DropColumn(
                name: "key_message",
                table: "slide_items");
        }
    }
}
