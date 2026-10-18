using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddTestBulkImportAndSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentSectionOrder",
                table: "StudentTestResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentSectionStartedAt",
                table: "StudentTestResults",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SectionPlanJson",
                table: "StudentTestResults",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SectionName",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SectionOrder",
                table: "StudentAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTestOnly",
                table: "QuestionBankQuestions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TestImportJobId",
                table: "QuestionBankQuestions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportJobId",
                table: "PracticeTestTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportJobId",
                table: "MockTests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSectional",
                table: "MockTests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ResultDelayMinutes",
                table: "MockTests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultReleaseAt",
                table: "MockTests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultReleaseMode",
                table: "MockTests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Instant");

            migrationBuilder.AddColumn<Guid>(
                name: "SectionId",
                table: "MockTestQuestions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MockTestSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MockTestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SectionOrder = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockTestSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockTestSections_MockTests_MockTestId",
                        column: x => x.MockTestId,
                        principalTable: "MockTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TotalTests = table.Column<int>(type: "int", nullable: false),
                    ValidTests = table.Column<int>(type: "int", nullable: false),
                    InvalidTests = table.Column<int>(type: "int", nullable: false),
                    ImportedTests = table.Column<int>(type: "int", nullable: false),
                    NewQuestionsCreated = table.Column<int>(type: "int", nullable: false),
                    ExistingQuestionsReused = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RolledBackAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestImportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestImportJobs_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankQuestions_TestImportJobId",
                table: "QuestionBankQuestions",
                column: "TestImportJobId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeTestTemplates_ImportJobId",
                table: "PracticeTestTemplates",
                column: "ImportJobId");

            migrationBuilder.CreateIndex(
                name: "IX_MockTests_ImportJobId",
                table: "MockTests",
                column: "ImportJobId");

            migrationBuilder.CreateIndex(
                name: "IX_MockTestQuestions_SectionId",
                table: "MockTestQuestions",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MockTestSections_MockTestId_SectionOrder",
                table: "MockTestSections",
                columns: new[] { "MockTestId", "SectionOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TestImportJobs_CreatedAt",
                table: "TestImportJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TestImportJobs_CreatedByAdminId",
                table: "TestImportJobs",
                column: "CreatedByAdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_MockTestQuestions_MockTestSections_SectionId",
                table: "MockTestQuestions",
                column: "SectionId",
                principalTable: "MockTestSections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MockTestQuestions_MockTestSections_SectionId",
                table: "MockTestQuestions");

            migrationBuilder.DropTable(
                name: "MockTestSections");

            migrationBuilder.DropTable(
                name: "TestImportJobs");

            migrationBuilder.DropIndex(
                name: "IX_QuestionBankQuestions_TestImportJobId",
                table: "QuestionBankQuestions");

            migrationBuilder.DropIndex(
                name: "IX_PracticeTestTemplates_ImportJobId",
                table: "PracticeTestTemplates");

            migrationBuilder.DropIndex(
                name: "IX_MockTests_ImportJobId",
                table: "MockTests");

            migrationBuilder.DropIndex(
                name: "IX_MockTestQuestions_SectionId",
                table: "MockTestQuestions");

            migrationBuilder.DropColumn(
                name: "CurrentSectionOrder",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "CurrentSectionStartedAt",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "SectionPlanJson",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "SectionName",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "SectionOrder",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "IsTestOnly",
                table: "QuestionBankQuestions");

            migrationBuilder.DropColumn(
                name: "TestImportJobId",
                table: "QuestionBankQuestions");

            migrationBuilder.DropColumn(
                name: "ImportJobId",
                table: "PracticeTestTemplates");

            migrationBuilder.DropColumn(
                name: "ImportJobId",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "IsSectional",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "ResultDelayMinutes",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "ResultReleaseAt",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "ResultReleaseMode",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "MockTestQuestions");
        }
    }
}
