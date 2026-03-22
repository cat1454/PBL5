using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    public partial class AddSlideDecks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "slide_decks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    subtitle = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    theme_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    outline = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slide_decks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_slide_decks_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "slide_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slide_deck_id = table.Column<int>(type: "integer", nullable: false),
                    slide_index = table.Column<int>(type: "integer", nullable: false),
                    slide_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    heading = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    subheading = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    goal = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    body = table.Column<string>(type: "jsonb", nullable: true),
                    speaker_notes = table.Column<string>(type: "text", nullable: true),
                    accent_tone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slide_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_slide_items_slide_decks_slide_deck_id",
                        column: x => x.slide_deck_id,
                        principalTable: "slide_decks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_slide_decks_document_id",
                table: "slide_decks",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_slide_decks_document_id_created_at",
                table: "slide_decks",
                columns: new[] { "document_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_slide_decks_status",
                table: "slide_decks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_slide_items_slide_deck_id",
                table: "slide_items",
                column: "slide_deck_id");

            migrationBuilder.CreateIndex(
                name: "IX_slide_items_slide_deck_id_slide_index",
                table: "slide_items",
                columns: new[] { "slide_deck_id", "slide_index" });

            migrationBuilder.CreateIndex(
                name: "IX_slide_items_status",
                table: "slide_items",
                column: "status");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "slide_items");

            migrationBuilder.DropTable(
                name: "slide_decks");
        }
    }
}
