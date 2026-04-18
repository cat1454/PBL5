using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderProjectsFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "editor_state",
                table: "slide_items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "document_id",
                table: "slide_decks",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "folder_project_id",
                table: "slide_decks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "folder_project_id",
                table: "documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "folder_source_order",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "include_in_folder_slides",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "folder_projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    description = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    uploaded_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folder_projects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_slide_decks_folder_project_id",
                table: "slide_decks",
                column: "folder_project_id");

            migrationBuilder.CreateIndex(
                name: "IX_slide_decks_folder_project_id_created_at",
                table: "slide_decks",
                columns: new[] { "folder_project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_folder_project_id",
                table: "documents",
                column: "folder_project_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_folder_project_id_folder_source_order",
                table: "documents",
                columns: new[] { "folder_project_id", "folder_source_order" });

            migrationBuilder.CreateIndex(
                name: "IX_folder_projects_created_at",
                table: "folder_projects",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_folder_projects_updated_at",
                table: "folder_projects",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_folder_projects_uploaded_by",
                table: "folder_projects",
                column: "uploaded_by");

            migrationBuilder.AddForeignKey(
                name: "FK_documents_folder_projects_folder_project_id",
                table: "documents",
                column: "folder_project_id",
                principalTable: "folder_projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_slide_decks_folder_projects_folder_project_id",
                table: "slide_decks",
                column: "folder_project_id",
                principalTable: "folder_projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_documents_folder_projects_folder_project_id",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_slide_decks_folder_projects_folder_project_id",
                table: "slide_decks");

            migrationBuilder.DropTable(
                name: "folder_projects");

            migrationBuilder.DropIndex(
                name: "IX_slide_decks_folder_project_id",
                table: "slide_decks");

            migrationBuilder.DropIndex(
                name: "IX_slide_decks_folder_project_id_created_at",
                table: "slide_decks");

            migrationBuilder.DropIndex(
                name: "IX_documents_folder_project_id",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_folder_project_id_folder_source_order",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "editor_state",
                table: "slide_items");

            migrationBuilder.DropColumn(
                name: "folder_project_id",
                table: "slide_decks");

            migrationBuilder.DropColumn(
                name: "folder_project_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "folder_source_order",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "include_in_folder_slides",
                table: "documents");

            migrationBuilder.AlterColumn<int>(
                name: "document_id",
                table: "slide_decks",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
