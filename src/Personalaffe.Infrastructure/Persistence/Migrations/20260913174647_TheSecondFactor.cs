using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheSecondFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_totp_step",
                table: "owner",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_totp_secret",
                table: "owner",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_totp_secret_at",
                table: "owner",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "totp_enrolled_at",
                table: "owner",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "totp_secret",
                table: "owner",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recovery_code",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recovery_code", x => x.id);
                    table.ForeignKey(
                        name: "fk_recovery_code_owner",
                        column: x => x.owner_id,
                        principalTable: "owner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "recovery_code_hash",
                table: "recovery_code",
                columns: new[] { "owner_id", "code_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recovery_code");

            migrationBuilder.DropColumn(
                name: "last_totp_step",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "pending_totp_secret",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "pending_totp_secret_at",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "totp_enrolled_at",
                table: "owner");

            migrationBuilder.DropColumn(
                name: "totp_secret",
                table: "owner");
        }
    }
}
