using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    public partial class AddSlideImageMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_candidates",
                table: "slide_items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_plan",
                table: "slide_items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_image_key",
                table: "slide_items",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_candidates",
                table: "slide_items");

            migrationBuilder.DropColumn(
                name: "image_plan",
                table: "slide_items");

            migrationBuilder.DropColumn(
                name: "selected_image_key",
                table: "slide_items");
        }
    }
}
