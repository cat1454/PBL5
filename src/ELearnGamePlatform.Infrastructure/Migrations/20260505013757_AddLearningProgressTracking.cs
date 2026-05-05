using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningProgressTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "learning_attempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    selected_answer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    response_time_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_attempts_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_learning_attempts_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_progresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    wrong_count = table.Column<int>(type: "integer", nullable: false),
                    current_streak = table.Column<int>(type: "integer", nullable: false),
                    best_streak = table.Column<int>(type: "integer", nullable: false),
                    last_reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    memory_score = table.Column<double>(type: "double precision", nullable: false),
                    mastery_score = table.Column<double>(type: "double precision", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_progresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_progresses_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_learning_progresses_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_learning_attempts_document_id",
                table: "learning_attempts",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_attempts_question_id",
                table: "learning_attempts",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_attempts_user_id",
                table: "learning_attempts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_attempts_user_id_document_id_created_at",
                table: "learning_attempts",
                columns: new[] { "user_id", "document_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_attempts_user_id_document_id_question_id",
                table: "learning_attempts",
                columns: new[] { "user_id", "document_id", "question_id" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_progresses_document_id",
                table: "learning_progresses",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_progresses_question_id",
                table: "learning_progresses",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_progresses_user_id",
                table: "learning_progresses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_progresses_user_id_document_id",
                table: "learning_progresses",
                columns: new[] { "user_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_progresses_user_id_document_id_question_id",
                table: "learning_progresses",
                columns: new[] { "user_id", "document_id", "question_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_attempts");

            migrationBuilder.DropTable(
                name: "learning_progresses");
        }
    }
}
