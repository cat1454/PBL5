using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClassroomWorkspaceModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "classroom_workspaces",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    description = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    owner_user_id = table.Column<int>(type: "integer", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_workspaces", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_workspaces_app_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "classroom_join_codes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    classroom_workspace_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_join_codes", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_join_codes_app_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_classroom_join_codes_classroom_workspaces_classroom_workspa~",
                        column: x => x.classroom_workspace_id,
                        principalTable: "classroom_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classroom_members",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    classroom_workspace_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_classroom_members_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_classroom_members_classroom_workspaces_classroom_workspace_~",
                        column: x => x.classroom_workspace_id,
                        principalTable: "classroom_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_classroom_join_codes_classroom_workspace_id_is_active",
                table: "classroom_join_codes",
                columns: new[] { "classroom_workspace_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_classroom_join_codes_code",
                table: "classroom_join_codes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classroom_join_codes_created_by_user_id",
                table: "classroom_join_codes",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_members_classroom_workspace_id_user_id",
                table: "classroom_members",
                columns: new[] { "classroom_workspace_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classroom_members_role",
                table: "classroom_members",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_members_status",
                table: "classroom_members",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_members_user_id",
                table: "classroom_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_workspaces_created_at",
                table: "classroom_workspaces",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_workspaces_owner_user_id",
                table: "classroom_workspaces",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_workspaces_updated_at",
                table: "classroom_workspaces",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "classroom_join_codes");

            migrationBuilder.DropTable(
                name: "classroom_members");

            migrationBuilder.DropTable(
                name: "classroom_workspaces");
        }
    }
}
