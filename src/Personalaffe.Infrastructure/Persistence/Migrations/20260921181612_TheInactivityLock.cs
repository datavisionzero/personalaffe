using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheInactivityLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "inactivity_lock_minutes",
                table: "owner",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "inactivity_lock_pin_hash",
                table: "owner",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "inactivity_lock_version",
                table: "owner",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "inactivity_lock_minutes",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "inactivity_lock_pin_hash",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "inactivity_lock_version",
                table: "owner");
        }
    }
}
