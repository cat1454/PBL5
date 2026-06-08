using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClassroomEmpiricalDifficultyScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add scoring config columns to classroom_assignments
            migrationBuilder.AddColumn<int>(
                name: "scoring_mode",
                table: "classroom_assignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "min_question_weight",
                table: "classroom_assignments",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 0.3m);

            migrationBuilder.AddColumn<decimal>(
                name: "max_question_weight",
                table: "classroom_assignments",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 2.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "smoothing_alpha",
                table: "classroom_assignments",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "smoothing_beta",
                table: "classroom_assignments",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 1m);

            // Create classroom_assignment_question_stats table
            migrationBuilder.CreateTable(
                name: "classroom_assignment_question_stats",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    classroom_assignment_id = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    answered_count = table.Column<int>(type: "integer", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    smoothed_correct_rate = table.Column<decimal>(type: "numeric(8,6)", nullable: false),
                    difficulty_weight = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                    discrimination_index = table.Column<decimal>(type: "numeric(8,4)", nullable: true),
                    quality_flag = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    calculated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_assignment_question_stats", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_assignment_question_stats_classroom_assignments_c~",
                        column: x => x.classroom_assignment_id,
                        principalTable: "classroom_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_classroom_assignment_question_stats_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignment_question_stats_classroom_assignment_id",
                table: "classroom_assignment_question_stats",
                column: "classroom_assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignment_question_stats_classroom_assignment_id_question_id",
                table: "classroom_assignment_question_stats",
                columns: new[] { "classroom_assignment_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classroom_assignment_question_stats_question_id",
                table: "classroom_assignment_question_stats",
                column: "question_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "classroom_assignment_question_stats");

            migrationBuilder.DropColumn(
                name: "scoring_mode",
                table: "classroom_assignments");

            migrationBuilder.DropColumn(
                name: "min_question_weight",
                table: "classroom_assignments");

            migrationBuilder.DropColumn(
                name: "max_question_weight",
                table: "classroom_assignments");

            migrationBuilder.DropColumn(
                name: "smoothing_alpha",
                table: "classroom_assignments");

            migrationBuilder.DropColumn(
                name: "smoothing_beta",
                table: "classroom_assignments");
        }
    }
}
