using System;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524050000_AddQuestionStudioV2")]
    public partial class AddQuestionStudioV2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "quality_score",
                table: "questions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_draft_id",
                table: "questions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "question_generation_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    stage = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    target_draft_count = table.Column<int>(type: "integer", nullable: false),
                    generated_draft_count = table.Column<int>(type: "integer", nullable: false),
                    verified_draft_count = table.Column<int>(type: "integer", nullable: false),
                    imported_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    rejected_count = table.Column<int>(type: "integer", nullable: false),
                    borderline_count = table.Column<int>(type: "integer", nullable: false),
                    quarantined_count = table.Column<int>(type: "integer", nullable: false),
                    requested_question_types = table.Column<string>(type: "jsonb", nullable: false),
                    requested_difficulties = table.Column<string>(type: "jsonb", nullable: false),
                    model_profile = table.Column<string>(type: "jsonb", nullable: false),
                    failure_stats = table.Column<string>(type: "jsonb", nullable: false),
                    metrics = table.Column<string>(type: "jsonb", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_generation_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_question_generation_runs_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question_source_units",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    generation_run_id = table.Column<int>(type: "integer", nullable: true),
                    unit_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    topic_tag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    end_offset = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_source_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_question_source_units_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_question_source_units_question_generation_runs_generation_run_id",
                        column: x => x.generation_run_id,
                        principalTable: "question_generation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question_drafts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    generation_run_id = table.Column<int>(type: "integer", nullable: false),
                    source_unit_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    draft_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    parent_draft_id = table.Column<int>(type: "integer", nullable: true),
                    question_text = table.Column<string>(type: "text", nullable: false),
                    question_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    correct_answer = table.Column<string>(type: "text", nullable: false),
                    explanation = table.Column<string>(type: "text", nullable: false),
                    difficulty = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    learning_objective = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    topic_tag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    grounding_score = table.Column<double>(type: "double precision", nullable: false),
                    answer_score = table.Column<double>(type: "double precision", nullable: false),
                    clarity_score = table.Column<double>(type: "double precision", nullable: false),
                    duplicate_score = table.Column<double>(type: "double precision", nullable: false),
                    overall_score = table.Column<double>(type: "double precision", nullable: false),
                    repair_count = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: false),
                    source_evidence = table.Column<string>(type: "text", nullable: false),
                    stem_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_drafts", x => x.id);
                    table.ForeignKey(
                        name: "fk_question_drafts_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_question_drafts_question_drafts_parent_draft_id",
                        column: x => x.parent_draft_id,
                        principalTable: "question_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_question_drafts_question_generation_runs_generation_run_id",
                        column: x => x.generation_run_id,
                        principalTable: "question_generation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_question_drafts_question_source_units_source_unit_id",
                        column: x => x.source_unit_id,
                        principalTable: "question_source_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "question_review_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    question_draft_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: false),
                    after = table.Column<string>(type: "jsonb", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_review_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_question_review_events_question_drafts_question_draft_id",
                        column: x => x.question_draft_id,
                        principalTable: "question_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "ix_question_generation_runs_document_id_created_at", table: "question_generation_runs", columns: new[] { "document_id", "created_at" });
            migrationBuilder.CreateIndex(name: "ix_question_generation_runs_status", table: "question_generation_runs", column: "status");
            migrationBuilder.CreateIndex(name: "ix_questions_source_draft_id", table: "questions", column: "source_draft_id", unique: true, filter: "source_draft_id IS NOT NULL");
            migrationBuilder.CreateIndex(name: "ix_question_source_units_document_id_topic_tag", table: "question_source_units", columns: new[] { "document_id", "topic_tag" });
            migrationBuilder.CreateIndex(name: "ix_question_source_units_generation_run_id", table: "question_source_units", column: "generation_run_id");
            migrationBuilder.CreateIndex(name: "ix_question_source_units_source_hash", table: "question_source_units", column: "source_hash");
            migrationBuilder.CreateIndex(name: "ix_question_drafts_document_id_status", table: "question_drafts", columns: new[] { "document_id", "status" });
            migrationBuilder.CreateIndex(name: "ix_question_drafts_generation_run_id_status", table: "question_drafts", columns: new[] { "generation_run_id", "status" });
            migrationBuilder.CreateIndex(name: "ix_question_drafts_parent_draft_id", table: "question_drafts", column: "parent_draft_id");
            migrationBuilder.CreateIndex(name: "ix_question_drafts_source_unit_id", table: "question_drafts", column: "source_unit_id");
            migrationBuilder.CreateIndex(name: "ix_question_drafts_stem_hash", table: "question_drafts", column: "stem_hash");
            migrationBuilder.CreateIndex(name: "ix_question_drafts_topic_tag_difficulty", table: "question_drafts", columns: new[] { "topic_tag", "difficulty" });
            migrationBuilder.CreateIndex(name: "ix_question_review_events_action", table: "question_review_events", column: "action");
            migrationBuilder.CreateIndex(name: "ix_question_review_events_question_draft_id", table: "question_review_events", column: "question_draft_id");
            migrationBuilder.CreateIndex(name: "ix_question_review_events_question_draft_id_created_at", table: "question_review_events", columns: new[] { "question_draft_id", "created_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "question_review_events");
            migrationBuilder.DropTable(name: "question_drafts");
            migrationBuilder.DropTable(name: "question_source_units");
            migrationBuilder.DropTable(name: "question_generation_runs");
            migrationBuilder.DropIndex(name: "ix_questions_source_draft_id", table: "questions");
            migrationBuilder.DropColumn(name: "quality_score", table: "questions");
            migrationBuilder.DropColumn(name: "source_draft_id", table: "questions");
        }
    }
}
