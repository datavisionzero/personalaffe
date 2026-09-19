using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheInstanceAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instance_appearance",
                columns: table => new
                {
                    singleton = table.Column<bool>(type: "boolean", nullable: false),
                    title = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    colour = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    shape = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instance_appearance", x => x.singleton);
                    table.CheckConstraint("ck_instance_appearance_singleton", "singleton");
                });

            migrationBuilder.InsertData(
                table: "instance_appearance",
                columns: new[] { "singleton", "colour", "shape", "title", "updated_at" },
                values: new object[] { true, "violet", "square", null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instance_appearance");
        }
    }
}
