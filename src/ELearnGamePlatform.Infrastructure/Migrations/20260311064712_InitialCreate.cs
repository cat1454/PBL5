using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    file_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    extracted_text = table.Column<string>(type: "text", nullable: true),
                    main_topics = table.Column<string>(type: "jsonb", nullable: true),
                    key_points = table.Column<string>(type: "jsonb", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "game_sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    game_type = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    question_ids = table.Column<string>(type: "jsonb", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: false),
                    total_questions = table.Column<int>(type: "integer", nullable: false),
                    correct_answers = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_sessions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    question_text = table.Column<string>(type: "text", nullable: false),
                    question_type = table.Column<int>(type: "integer", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: true),
                    correct_answer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    explanation = table.Column<string>(type: "text", nullable: true),
                    difficulty = table.Column<int>(type: "integer", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_questions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_documents_created_at",
                table: "documents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_documents_status",
                table: "documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_documents_uploaded_by",
                table: "documents",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_document_id",
                table: "game_sessions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_user_id",
                table: "game_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_sessions_user_id_created_at",
                table: "game_sessions",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_questions_document_id",
                table: "questions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_questions_document_id_question_type",
                table: "questions",
                columns: new[] { "document_id", "question_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_sessions");

            migrationBuilder.DropTable(
                name: "questions");

            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
