using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // STUDY PARTNER CHALLENGES -- students now type their own duration (1 minute .. 30 days). Purely ADDITIVE:
    // one NULLABLE int column. Existing challenges keep DurationDays and run DurationDays * 1440 minutes
    // (the API treats NULL that way), so no data needs backfilling.
    // ROLLBACK: `dotnet ef database update 20261019000100_AddStudentReviewsAndLandingContent` drops just this column.
    //
    // NOTE: written by hand (no .Designer.cs), like the two migrations before it; the model snapshot was
    // updated by hand to match, so the next `dotnet ef migrations add` should come out empty.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261021000000_AddChallengeDurationMinutes")]
    public partial class AddChallengeDurationMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "StudyPartnerChallenges",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "StudyPartnerChallenges");
        }
    }
}
