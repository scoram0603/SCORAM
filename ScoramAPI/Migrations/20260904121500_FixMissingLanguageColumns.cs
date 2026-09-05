using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    // Production reality check (Sep 4, 2026): "20260826044218_AddLanguageToMockTestAndQuiz" is a
    // no-op migration -- its own comment says the Language column was "added by an earlier migration
    // attempt that got applied to the database before its own .cs file was lost", and that this
    // migration exists only to bring migration HISTORY back in sync, not the schema. That assumption
    // was wrong for this database: the live app is throwing
    //   Microsoft.Data.SqlClient.SqlException: Invalid column name 'Language'.
    // from GET /api/tests/attempts/{id} (LoadOwnedAttemptAsync, which Includes MockTest and, via
    // Quiz-kind attempts, Quiz) -- the column was never actually created here, so recording that old
    // migration as "already applied" (a no-op Up()) left this database permanently short the column
    // with no future migration ever adding it.
    //
    // This migration adds MockTests.Language and Quizzes.Language for real, but conditionally -- using
    // COL_LENGTH to check first -- so it's safe to run on EVERY environment regardless of which one of
    // the two states it's currently in: a dev DB where the column genuinely does already exist (skips,
    // same as before) or this DB where it doesn't (actually adds it, finally matching the EF model in
    // ScoramDbContextModelSnapshot.cs, which has always expected both columns to be there).
    public partial class FixMissingLanguageColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF COL_LENGTH('MockTests', 'Language') IS NULL
                BEGIN
                    ALTER TABLE [MockTests] ADD [Language] int NULL;
                END;
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('Quizzes', 'Language') IS NULL
                BEGIN
                    ALTER TABLE [Quizzes] ADD [Language] int NULL;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op: the previous migration's Down() already drops both columns, and
            // running that Down() after this one is what actually gets used if this migration is ever
            // rolled back on its own -- there's nothing left for this migration's Down() to do that
            // wouldn't just be an unconditional DropColumn duplicating that one.
        }
    }
}
