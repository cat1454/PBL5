using System;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260525090000_AddLearningConfidenceAnalytics")]
    public partial class AddLearningConfidenceAnalytics : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "confidence",
                table: "learning_attempts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "analytics_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    properties_json = table.Column<string>(type: "jsonb", nullable: false),
                    session_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_name",
                table: "analytics_events",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_received_at",
                table: "analytics_events",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_user_id",
                table: "analytics_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_user_id_received_at",
                table: "analytics_events",
                columns: new[] { "user_id", "received_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_events");

            migrationBuilder.DropColumn(
                name: "confidence",
                table: "learning_attempts");
        }
    }
}
