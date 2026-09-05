using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    // Adds the tracking Question Bank bulk-import rollback needs (spec: "Roll back" should work for
    // Question Bank imports the same way it already does for PYP/Paper imports, including cleaning
    // up any exam that was created just for this import):
    //   - QuestionBankExamMappings.ImportJobId -- tags a MERGE-created mapping (an exam/year tag
    //     added to an ALREADY-EXISTING question) with the job that added it, so a rollback can find
    //     and remove exactly that mapping without touching the question itself. A brand-new
    //     question's own mappings don't need this -- QuestionBankQuestion.ImportJobId already
    //     identifies the job, and deleting the question cascades to its mappings automatically.
    //   - QuestionBankImportJobs.CandidateEmptyExamIds -- comma-separated Guid list of exams that had
    //     no other content right before this job's commit gave them their first content (mirrors
    //     Paper.ExamCreatedForThisPaper, generalized to a list since one Question Bank import can
    //     span several exams, unlike a Paper which only ever has one).
    //   - QuestionBankImportJobs.RolledBackAt -- mirrors ImportJob.RolledBackAt (the Paper-side
    //     equivalent), set when QuestionBankAdminController.Rollback runs.
    public partial class AddQuestionBankImportRollbackSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportJobId",
                table: "QuestionBankExamMappings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CandidateEmptyExamIds",
                table: "QuestionBankImportJobs",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RolledBackAt",
                table: "QuestionBankImportJobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankExamMappings_ImportJobId",
                table: "QuestionBankExamMappings",
                column: "ImportJobId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionBankExamMappings_QuestionBankImportJobs_ImportJobId",
                table: "QuestionBankExamMappings",
                column: "ImportJobId",
                principalTable: "QuestionBankImportJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestionBankExamMappings_QuestionBankImportJobs_ImportJobId",
                table: "QuestionBankExamMappings");

            migrationBuilder.DropIndex(
                name: "IX_QuestionBankExamMappings_ImportJobId",
                table: "QuestionBankExamMappings");

            migrationBuilder.DropColumn(
                name: "ImportJobId",
                table: "QuestionBankExamMappings");

            migrationBuilder.DropColumn(
                name: "CandidateEmptyExamIds",
                table: "QuestionBankImportJobs");

            migrationBuilder.DropColumn(
                name: "RolledBackAt",
                table: "QuestionBankImportJobs");
        }
    }
}
