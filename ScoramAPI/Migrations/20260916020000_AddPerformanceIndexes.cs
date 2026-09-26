using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- /api/questions (QuestionsController.Search) ----------
            // Every one of these backs an optional equality filter in that endpoint, or (CreatedAt)
            // its default sort when no PaperId is given -- all were previously unindexed, meaning a
            // full Questions table scan (+ full sort, for the CreatedAt case) on every search that
            // didn't filter down to a single Paper or Exam.
            migrationBuilder.CreateIndex(name: "IX_Questions_Subject", table: "Questions", column: "Subject");
            migrationBuilder.CreateIndex(name: "IX_Questions_Topic", table: "Questions", column: "Topic");
            migrationBuilder.CreateIndex(name: "IX_Questions_Year", table: "Questions", column: "Year");
            migrationBuilder.CreateIndex(name: "IX_Questions_DifficultyLevel", table: "Questions", column: "DifficultyLevel");
            migrationBuilder.CreateIndex(name: "IX_Questions_ExamName", table: "Questions", column: "ExamName");
            migrationBuilder.CreateIndex(name: "IX_Questions_CreatedAt", table: "Questions", column: "CreatedAt");

            // Used in EXISTS-style correlated subqueries run once per row in
            // QuestionBankController.VisibleQuestions() and once per exam in ExamsController's bank
            // question-count aggregate. Unindexed, this was an O(rows in QuestionBankQuestions x rows
            // in Questions) scan pattern -- almost certainly the single biggest cause of
            // /api/question-bank/search's measured 25-48s responses / timeouts. This index alone turns
            // each correlated check into an index seek without needing to restructure that query's
            // (deliberately reasoned-through -- see VisibleQuestions' own comment) logic.
            migrationBuilder.CreateIndex(name: "IX_Questions_MirroredToQuestionBankQuestionId", table: "Questions", column: "MirroredToQuestionBankQuestionId");

            // ---------- /api/question-bank/search (QuestionBankController.Search) ----------
            // VisibleQuestions()'s base filter is always IsActive == true, and the default sort is
            // always CreatedAt descending -- composite so both are served by one index instead of an
            // index seek on IsActive followed by an in-memory sort of everything it matches.
            migrationBuilder.CreateIndex(name: "IX_QuestionBankQuestions_IsActive_CreatedAt", table: "QuestionBankQuestions", columns: new[] { "IsActive", "CreatedAt" });
            migrationBuilder.CreateIndex(name: "IX_QuestionBankQuestions_Language", table: "QuestionBankQuestions", column: "Language");

            // NOTE: no index added for QuestionBankExamMappings' ExamId/Year filters -- both columns
            // already have their own individual index plus a composite unique index, confirmed by
            // reading that entity's existing configuration. Would have been redundant write overhead.

            // ---------- /api/admin/papers/pending (PapersController.ListPending) ----------
            migrationBuilder.CreateIndex(name: "IX_Papers_Status", table: "Papers", column: "Status");

            // ---------- /api/admin/solutions/pending (SolutionsController.GetPending) ----------
            // Composite, not IsApproved alone: this table only grows (an approved solution is never
            // deleted), so an IsApproved-only index keeps getting less selective over time as the
            // always-small pending minority shrinks relative to the ever-growing approved majority --
            // the composite also covers the CreatedAt sort this endpoint always applies.
            migrationBuilder.CreateIndex(name: "IX_QuestionSolutions_IsApproved_CreatedAt", table: "QuestionSolutions", columns: new[] { "IsApproved", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Questions_Subject", table: "Questions");
            migrationBuilder.DropIndex(name: "IX_Questions_Topic", table: "Questions");
            migrationBuilder.DropIndex(name: "IX_Questions_Year", table: "Questions");
            migrationBuilder.DropIndex(name: "IX_Questions_DifficultyLevel", table: "Questions");
            migrationBuilder.DropIndex(name: "IX_Questions_ExamName", table: "Questions");
            migrationBuilder.DropIndex(name: "IX_Questions_CreatedAt", table: "Questions");
            migrationBuilder.DropIndex(name: "IX_Questions_MirroredToQuestionBankQuestionId", table: "Questions");
            migrationBuilder.DropIndex(name: "IX_QuestionBankQuestions_IsActive_CreatedAt", table: "QuestionBankQuestions");
            migrationBuilder.DropIndex(name: "IX_QuestionBankQuestions_Language", table: "QuestionBankQuestions");
            migrationBuilder.DropIndex(name: "IX_Papers_Status", table: "Papers");
            migrationBuilder.DropIndex(name: "IX_QuestionSolutions_IsApproved_CreatedAt", table: "QuestionSolutions");
        }
    }
}
