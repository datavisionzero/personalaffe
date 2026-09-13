using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheRecoveryOnTheServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recovered_at",
                table: "owner",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recovered_at",
                table: "owner");
        }
    }
}
