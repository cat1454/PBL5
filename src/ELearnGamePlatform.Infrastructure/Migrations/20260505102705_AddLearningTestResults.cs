using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningTestResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "test_result_id",
                table: "learning_attempts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "learning_test_results",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    total_questions = table.Column<int>(type: "integer", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    wrong_count = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    test_type = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_test_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_test_results_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_learning_attempts_test_result_id",
                table: "learning_attempts",
                column: "test_result_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_test_results_document_id",
                table: "learning_test_results",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_test_results_submitted_at",
                table: "learning_test_results",
                column: "submitted_at");

            migrationBuilder.CreateIndex(
                name: "IX_learning_test_results_test_type",
                table: "learning_test_results",
                column: "test_type");

            migrationBuilder.CreateIndex(
                name: "IX_learning_test_results_user_id",
                table: "learning_test_results",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_learning_test_results_user_id_document_id",
                table: "learning_test_results",
                columns: new[] { "user_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_test_results_user_id_document_id_submitted_at",
                table: "learning_test_results",
                columns: new[] { "user_id", "document_id", "submitted_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_learning_attempts_learning_test_results_test_result_id",
                table: "learning_attempts",
                column: "test_result_id",
                principalTable: "learning_test_results",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_learning_attempts_learning_test_results_test_result_id",
                table: "learning_attempts");

            migrationBuilder.DropTable(
                name: "learning_test_results");

            migrationBuilder.DropIndex(
                name: "IX_learning_attempts_test_result_id",
                table: "learning_attempts");

            migrationBuilder.DropColumn(
                name: "test_result_id",
                table: "learning_attempts");
        }
    }
}
