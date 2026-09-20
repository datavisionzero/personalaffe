using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bookmarks",
                table: "agent_access",
                type: "text",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.CreateTable(
                name: "bookmark_folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    @private = table.Column<bool>(name: "private", type: "boolean", nullable: false),
                    private_origin = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_kind = table.Column<string>(type: "text", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookmark_folders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bookmarks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    favorite_position = table.Column<double>(type: "double precision", nullable: true),
                    private_origin = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_kind = table.Column<string>(type: "text", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookmarks", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "application",
                columns: new[] { "application", "enabled", "updated_at" },
                values: new object[] { "bookmarks", true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.AddCheckConstraint(
                name: "ck_agent_access_bookmarks",
                table: "agent_access",
                sql: "bookmarks in ('none', 'read', 'read_write')");

            migrationBuilder.CreateIndex(
                name: "IX_bookmark_folders_deleted_at",
                table: "bookmark_folders",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "IX_bookmark_folders_parent_id",
                table: "bookmark_folders",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookmarks_deleted_at",
                table: "bookmarks",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "IX_bookmarks_folder_id",
                table: "bookmarks",
                column: "folder_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookmark_folders");

            migrationBuilder.DropTable(
                name: "bookmarks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_agent_access_bookmarks",
                table: "agent_access");

            migrationBuilder.DeleteData(
                table: "application",
                keyColumn: "application",
                keyValue: "bookmarks");

            migrationBuilder.DropColumn(
                name: "bookmarks",
                table: "agent_access");
        }
    }
}
