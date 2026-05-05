using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StabilizeLearningTestSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "question_ids",
                table: "learning_test_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_snapshot",
                table: "learning_test_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "learning_test_results",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<Guid>(
                name: "test_session_id",
                table: "learning_test_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE learning_test_results SET test_session_id = md5(\"Id\"::text || ':learning-test-session')::uuid WHERE test_session_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "test_session_id",
                table: "learning_test_results",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_test_results_status",
                table: "learning_test_results",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_learning_test_results_test_session_id",
                table: "learning_test_results",
                column: "test_session_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_learning_test_results_status",
                table: "learning_test_results");

            migrationBuilder.DropIndex(
                name: "IX_learning_test_results_test_session_id",
                table: "learning_test_results");

            migrationBuilder.DropColumn(
                name: "question_ids",
                table: "learning_test_results");

            migrationBuilder.DropColumn(
                name: "result_snapshot",
                table: "learning_test_results");

            migrationBuilder.DropColumn(
                name: "status",
                table: "learning_test_results");

            migrationBuilder.DropColumn(
                name: "test_session_id",
                table: "learning_test_results");
        }
    }
}
