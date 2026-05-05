using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepairMissingRuntimeSchemaColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
             migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                ADD COLUMN IF NOT EXISTS key_message character varying(400);
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                ADD COLUMN IF NOT EXISTS evidence_from_text text;
            """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                DROP COLUMN IF EXISTS evidence_from_text;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                DROP COLUMN IF EXISTS key_message;
            """);
        }
    }
}
