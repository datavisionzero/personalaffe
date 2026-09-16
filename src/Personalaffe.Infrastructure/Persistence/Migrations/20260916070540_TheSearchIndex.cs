using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "tasks",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', regexp_replace(coalesce(title, ''), '[^[:alnum:]]+', ' ', 'g')), 'A') || setweight(to_tsvector('simple', coalesce(description, '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "scratchpad_entries",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(text, '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "pages",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', regexp_replace(coalesce(title, ''), '[^[:alnum:]]+', ' ', 'g')), 'A') || setweight(to_tsvector('simple', coalesce(markdown, '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "files",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', regexp_replace(coalesce(name, ''), '[^[:alnum:]]+', ' ', 'g')), 'A')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_search",
                table: "tasks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_scratchpad_entries_search",
                table: "scratchpad_entries",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_pages_search",
                table: "pages",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_files_search",
                table: "files",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tasks_search",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_scratchpad_entries_search",
                table: "scratchpad_entries");

            migrationBuilder.DropIndex(
                name: "ix_pages_search",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "ix_files_search",
                table: "files");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "scratchpad_entries");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "files");
        }
    }
}
