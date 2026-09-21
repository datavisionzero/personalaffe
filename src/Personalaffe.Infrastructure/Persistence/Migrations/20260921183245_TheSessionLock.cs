using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheSessionLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_unlock_blocked_until",
                table: "owner",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "password_unlock_failures",
                table: "owner",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_unlock_window_started_at",
                table: "owner",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pin_unlock_blocked_until",
                table: "owner",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pin_unlock_failures",
                table: "owner",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pin_unlock_window_started_at",
                table: "owner",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "inactivity_lock_version",
                table: "browser_session",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inactivity_locked_at",
                table: "browser_session",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_interaction_at",
                table: "browser_session",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_unlock_blocked_until",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "password_unlock_failures",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "password_unlock_window_started_at",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "pin_unlock_blocked_until",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "pin_unlock_failures",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "pin_unlock_window_started_at",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "inactivity_lock_version",
                table: "browser_session");

            migrationBuilder.DropColumn(
                name: "inactivity_locked_at",
                table: "browser_session");

            migrationBuilder.DropColumn(
                name: "last_interaction_at",
                table: "browser_session");
        }
    }
}
