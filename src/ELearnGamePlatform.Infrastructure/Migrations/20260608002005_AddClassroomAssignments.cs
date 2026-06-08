using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClassroomAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "classroom_assignments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    classroom_workspace_id = table.Column<int>(type: "integer", nullable: false),
                    question_set_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    description = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    time_limit_minutes = table.Column<int>(type: "integer", nullable: true),
                    attempt_limit = table.Column<int>(type: "integer", nullable: false),
                    shuffle_questions = table.Column<bool>(type: "boolean", nullable: false),
                    shuffle_options = table.Column<bool>(type: "boolean", nullable: false),
                    show_answer_after_submit = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_assignments_app_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_classroom_assignments_classroom_question_sets_question_set_~",
                        column: x => x.question_set_id,
                        principalTable: "classroom_question_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_classroom_assignments_classroom_workspaces_classroom_worksp~",
                        column: x => x.classroom_workspace_id,
                        principalTable: "classroom_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classroom_assignment_attempts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    classroom_assignment_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    raw_score = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    percent_score = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    attempt_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_assignment_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_assignment_attempts_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_classroom_assignment_attempts_classroom_assignments_classro~",
                        column: x => x.classroom_assignment_id,
                        principalTable: "classroom_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classroom_assignment_answers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    attempt_id = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    selected_answer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    point_earned = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    time_spent_seconds = table.Column<int>(type: "integer", nullable: true),
                    answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_assignment_answers", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_assignment_answers_classroom_assignment_attempts_~",
                        column: x => x.attempt_id,
                        principalTable: "classroom_assignment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_classroom_assignment_answers_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignment_answers_attempt_id_question_id",
                table: "classroom_assignment_answers",
                columns: new[] { "attempt_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignment_answers_question_id",
                table: "classroom_assignment_answers",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignment_attempts_classroom_assignment_id_user_~",
                table: "classroom_assignment_attempts",
                columns: new[] { "classroom_assignment_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignment_attempts_status",
                table: "classroom_assignment_attempts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignment_attempts_user_id",
                table: "classroom_assignment_attempts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignments_classroom_workspace_id",
                table: "classroom_assignments",
                column: "classroom_workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignments_created_by_user_id",
                table: "classroom_assignments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignments_question_set_id",
                table: "classroom_assignments",
                column: "question_set_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignments_status",
                table: "classroom_assignments",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "classroom_assignment_answers");

            migrationBuilder.DropTable(
                name: "classroom_assignment_attempts");

            migrationBuilder.DropTable(
                name: "classroom_assignments");
        }
    }
}
