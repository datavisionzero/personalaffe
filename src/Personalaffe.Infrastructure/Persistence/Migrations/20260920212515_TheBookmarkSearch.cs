using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheBookmarkSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "bookmarks",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', regexp_replace(coalesce(title, ''), '[^[:alnum:]]+', ' ', 'g')), 'A') || setweight(to_tsvector('simple', coalesce(description, '')), 'B') || setweight(to_tsvector('simple', regexp_replace(coalesce(url, ''), '[^[:alnum:]]+', ' ', 'g')), 'B')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookmarks_search",
                table: "bookmarks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookmarks_search",
                table: "bookmarks");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "bookmarks");
        }
    }
}
