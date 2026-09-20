using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheBookmarkOpenings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookmark_open_days",
                columns: table => new
                {
                    bookmark_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    last_opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookmark_open_days", x => new { x.bookmark_id, x.day });
                    table.ForeignKey(
                        name: "FK_bookmark_open_days_bookmarks_bookmark_id",
                        column: x => x.bookmark_id,
                        principalTable: "bookmarks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bookmark_openings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bookmark_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookmark_openings", x => x.id);
                    table.ForeignKey(
                        name: "FK_bookmark_openings_bookmarks_bookmark_id",
                        column: x => x.bookmark_id,
                        principalTable: "bookmarks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bookmark_open_days_day",
                table: "bookmark_open_days",
                column: "day");

            migrationBuilder.CreateIndex(
                name: "IX_bookmark_openings_bookmark_id",
                table: "bookmark_openings",
                column: "bookmark_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookmark_open_days");

            migrationBuilder.DropTable(
                name: "bookmark_openings");
        }
    }
}
