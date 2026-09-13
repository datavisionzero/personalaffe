using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Personalaffe.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The schema begins empty, and this is the migration that says so.
    /// </summary>
    /// <remarks>
    /// It creates no table: the owner is PERSONAL-E2's, the content safeguards
    /// PERSONAL-E3's, and each of the four applications brings its own tables
    /// with it. What it does create is the migrations history, and a row in it
    /// that every later version can compare itself against — which is the whole
    /// of the migration path this foundation had to prove: applied to an empty
    /// database, found done on the next start, and refused by an older binary
    /// that has never heard of what came after it.
    ///
    /// A table invented here to give this migration something to do would be a
    /// shape nobody has had to live with yet, which is the one thing PERSONAL-2
    /// was told not to leave behind.
    /// </remarks>
    public partial class TheEmptySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
