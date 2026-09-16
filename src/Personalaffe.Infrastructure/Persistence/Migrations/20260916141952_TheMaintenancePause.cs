using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheMaintenancePause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_pause",
                columns: table => new
                {
                    singleton = table.Column<bool>(type: "boolean", nullable: false),
                    since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_pause", x => x.singleton);
                    table.CheckConstraint("ck_maintenance_pause_singleton", "singleton");
                });

            migrationBuilder.InsertData(
                table: "maintenance_pause",
                columns: new[] { "singleton", "since", "until", "updated_at" },
                values: new object[] { true, null, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_pause");
        }
    }
}
