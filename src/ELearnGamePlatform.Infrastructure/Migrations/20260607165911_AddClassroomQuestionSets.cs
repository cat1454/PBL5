using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClassroomQuestionSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "classroom_question_sets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    classroom_workspace_id = table.Column<int>(type: "integer", nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    description = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_question_sets", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_question_sets_app_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_classroom_question_sets_classroom_workspaces_classroom_work~",
                        column: x => x.classroom_workspace_id,
                        principalTable: "classroom_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_classroom_question_sets_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "classroom_question_set_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    classroom_question_set_id = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    point_weight = table.Column<double>(type: "double precision", nullable: false),
                    section_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_question_set_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_question_set_items_classroom_question_sets_classr~",
                        column: x => x.classroom_question_set_id,
                        principalTable: "classroom_question_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_classroom_question_set_items_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_classroom_question_set_items_classroom_question_set_id_ques~",
                table: "classroom_question_set_items",
                columns: new[] { "classroom_question_set_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classroom_question_set_items_question_id",
                table: "classroom_question_set_items",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_question_sets_classroom_workspace_id",
                table: "classroom_question_sets",
                column: "classroom_workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_question_sets_created_by_user_id",
                table: "classroom_question_sets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_question_sets_document_id",
                table: "classroom_question_sets",
                column: "document_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "classroom_question_set_items");

            migrationBuilder.DropTable(
                name: "classroom_question_sets");
        }
    }
}
