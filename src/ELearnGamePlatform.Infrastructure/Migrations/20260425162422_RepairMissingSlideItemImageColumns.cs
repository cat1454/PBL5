using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ELearnGamePlatform.Infrastructure.Migrations
{
    public partial class RepairMissingSlideItemImageColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                ADD COLUMN IF NOT EXISTS image_candidates jsonb;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                ADD COLUMN IF NOT EXISTS image_plan jsonb;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                ADD COLUMN IF NOT EXISTS editor_state jsonb;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                ADD COLUMN IF NOT EXISTS selected_image_key character varying(160);
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                ADD COLUMN IF NOT EXISTS evidence_debug text;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                ADD COLUMN IF NOT EXISTS processed_metadata text;
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                DROP COLUMN IF EXISTS image_candidates;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                DROP COLUMN IF EXISTS image_plan;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                DROP COLUMN IF EXISTS editor_state;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                DROP COLUMN IF EXISTS selected_image_key;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.slide_items
                DROP COLUMN IF EXISTS evidence_debug;
            """);

            migrationBuilder.Sql("""
                ALTER TABLE public.documents
                DROP COLUMN IF EXISTS processed_metadata;
            """);
        }
    }
}