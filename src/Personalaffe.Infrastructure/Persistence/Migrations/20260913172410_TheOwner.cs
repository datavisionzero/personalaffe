using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "owner",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    singleton = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owner", x => x.id);
                    table.CheckConstraint("ck_owner_singleton", "singleton");
                });

            migrationBuilder.CreateIndex(
                name: "owner_singleton",
                table: "owner",
                column: "singleton",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "owner");
        }
    }
}
