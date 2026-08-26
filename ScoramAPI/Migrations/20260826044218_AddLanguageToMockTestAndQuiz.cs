using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddLanguageToMockTestAndQuiz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Language column already exists on Quizzes and MockTests (added by an earlier
            // migration attempt that got applied to the database before its own .cs file was
            // lost -- see the "20260826040832_AddLanguageToMockTestAndQuiz" history entry).
            // This migration exists purely to bring the local project's migration history back
            // in sync with the database's actual schema -- Up() is intentionally a no-op.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "MockTests");
        }
    }
}