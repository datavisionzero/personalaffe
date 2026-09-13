using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheAgentAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_prefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    token_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    files = table.Column<string>(type: "text", nullable: false),
                    knowledge = table.Column<string>(type: "text", nullable: false),
                    scratchpad = table.Column<string>(type: "text", nullable: false),
                    tasks = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_access", x => x.id);
                    table.CheckConstraint("ck_agent_access_files", "files in ('none', 'read', 'read_write')");
                    table.CheckConstraint("ck_agent_access_knowledge", "knowledge in ('none', 'read', 'read_write')");
                    table.CheckConstraint("ck_agent_access_scratchpad", "scratchpad in ('none', 'read', 'read_write')");
                    table.CheckConstraint("ck_agent_access_tasks", "tasks in ('none', 'read', 'read_write')");
                });

            migrationBuilder.CreateIndex(
                name: "agent_access_token_hash",
                table: "agent_access",
                column: "token_hash",
                unique: true);

            // The one thing the model cannot say (AgentAccessConfiguration):
            // two agents called the same thing are two things nobody can tell
            // apart at the moment of revoking one, and "Deploy" and "deploy"
            // are the same thing to the person reading the list.
            migrationBuilder.Sql(
                """CREATE UNIQUE INDEX agent_access_name ON agent_access (lower(name))""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_access");
        }
    }
}
