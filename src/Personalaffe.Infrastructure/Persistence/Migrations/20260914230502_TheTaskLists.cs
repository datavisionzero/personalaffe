using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheTaskLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_kind = table.Column<string>(type: "text", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_lists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    position = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_kind = table.Column<string>(type: "text", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_lists_deleted_at",
                table: "task_lists",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_deleted_at",
                table: "tasks",
                column: "deleted_at",
                filter: "deleted_at is not null");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_list_id_position",
                table: "tasks",
                columns: new[] { "list_id", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_lists");

            migrationBuilder.DropTable(
                name: "tasks");
        }
    }
}
