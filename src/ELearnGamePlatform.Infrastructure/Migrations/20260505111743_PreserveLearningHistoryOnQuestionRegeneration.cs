using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveLearningHistoryOnQuestionRegeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_learning_attempts_questions_question_id",
                table: "learning_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_learning_progresses_questions_question_id",
                table: "learning_progresses");

            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                table: "questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_questions_is_archived",
                table: "questions",
                column: "is_archived");

            migrationBuilder.AddForeignKey(
                name: "FK_learning_attempts_questions_question_id",
                table: "learning_attempts",
                column: "question_id",
                principalTable: "questions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_learning_progresses_questions_question_id",
                table: "learning_progresses",
                column: "question_id",
                principalTable: "questions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_learning_attempts_questions_question_id",
                table: "learning_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_learning_progresses_questions_question_id",
                table: "learning_progresses");

            migrationBuilder.DropIndex(
                name: "IX_questions_is_archived",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "is_archived",
                table: "questions");

            migrationBuilder.AddForeignKey(
                name: "FK_learning_attempts_questions_question_id",
                table: "learning_attempts",
                column: "question_id",
                principalTable: "questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_learning_progresses_questions_question_id",
                table: "learning_progresses",
                column: "question_id",
                principalTable: "questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
