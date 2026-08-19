using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeAndMockTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudentTestResults_UserId",
                table: "StudentTestResults");

            migrationBuilder.DropIndex(
                name: "IX_StudentAnswers_StudentTestResultId",
                table: "StudentAnswers");

            migrationBuilder.AlterColumn<Guid>(
                name: "MockTestId",
                table: "StudentTestResults",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<decimal>(
                name: "NegativeMarkingRatio",
                table: "StudentTestResults",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PracticeDifficulty",
                table: "StudentTestResults",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PracticeDurationMinutes",
                table: "StudentTestResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PracticeExamId",
                table: "StudentTestResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PracticeSubjectId",
                table: "StudentTestResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PracticeTestTemplateId",
                table: "StudentTestResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PracticeTopicId",
                table: "StudentTestResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PracticeYearFrom",
                table: "StudentTestResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PracticeYearTo",
                table: "StudentTestResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "StudentTestResults",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "StudentTestResults",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TestKind",
                table: "StudentTestResults",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "StudentAnswers",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<DateTime>(
                name: "AnsweredAt",
                table: "StudentAnswers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectOptionSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExplanationSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMarkedForReview",
                table: "StudentAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OptionASnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OptionBSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OptionCSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OptionDSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionBankQuestionId",
                table: "StudentAnswers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuestionOrder",
                table: "StudentAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "QuestionTextSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubjectSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopicSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DifficultyLevel",
                table: "QuestionBankQuestions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndAt",
                table: "MockTests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "MockTests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "MockTests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "MockTests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "MockTestQuestions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionBankQuestionId",
                table: "MockTestQuestions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PracticeTestTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    YearFrom = table.Column<int>(type: "int", nullable: true),
                    YearTo = table.Column<int>(type: "int", nullable: true),
                    Difficulty = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    QuestionCount = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    NegativeMarkingRatio = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    IsRandomOrder = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeTestTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeTestTemplates_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PracticeTestTemplates_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PracticeTestTemplates_QuestionBankSubjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "QuestionBankSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PracticeTestTemplates_QuestionBankTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "QuestionBankTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PracticeTestTemplateQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PracticeTestTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuestionBankQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuestionOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeTestTemplateQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeTestTemplateQuestions_PracticeTestTemplates_PracticeTestTemplateId",
                        column: x => x.PracticeTestTemplateId,
                        principalTable: "PracticeTestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PracticeTestTemplateQuestions_QuestionBankQuestions_QuestionBankQuestionId",
                        column: x => x.QuestionBankQuestionId,
                        principalTable: "QuestionBankQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PracticeTestTemplateQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentTestResults_PracticeTestTemplateId",
                table: "StudentTestResults",
                column: "PracticeTestTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentTestResults_UserId_MockTestId_Status",
                table: "StudentTestResults",
                columns: new[] { "UserId", "MockTestId", "Status" },
                unique: true,
                filter: "[MockTestId] IS NOT NULL AND [Status] = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_QuestionBankQuestionId",
                table: "StudentAnswers",
                column: "QuestionBankQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_StudentTestResultId_QuestionOrder",
                table: "StudentAnswers",
                columns: new[] { "StudentTestResultId", "QuestionOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MockTestQuestions_QuestionBankQuestionId",
                table: "MockTestQuestions",
                column: "QuestionBankQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeTestTemplateQuestions_PracticeTestTemplateId",
                table: "PracticeTestTemplateQuestions",
                column: "PracticeTestTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeTestTemplateQuestions_QuestionBankQuestionId",
                table: "PracticeTestTemplateQuestions",
                column: "QuestionBankQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeTestTemplateQuestions_QuestionId",
                table: "PracticeTestTemplateQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeTestTemplates_CreatedByAdminId",
                table: "PracticeTestTemplates",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeTestTemplates_ExamId",
                table: "PracticeTestTemplates",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeTestTemplates_SubjectId",
                table: "PracticeTestTemplates",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeTestTemplates_TopicId",
                table: "PracticeTestTemplates",
                column: "TopicId");

            migrationBuilder.AddForeignKey(
                name: "FK_MockTestQuestions_QuestionBankQuestions_QuestionBankQuestionId",
                table: "MockTestQuestions",
                column: "QuestionBankQuestionId",
                principalTable: "QuestionBankQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentAnswers_QuestionBankQuestions_QuestionBankQuestionId",
                table: "StudentAnswers",
                column: "QuestionBankQuestionId",
                principalTable: "QuestionBankQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentTestResults_PracticeTestTemplates_PracticeTestTemplateId",
                table: "StudentTestResults",
                column: "PracticeTestTemplateId",
                principalTable: "PracticeTestTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MockTestQuestions_QuestionBankQuestions_QuestionBankQuestionId",
                table: "MockTestQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentAnswers_QuestionBankQuestions_QuestionBankQuestionId",
                table: "StudentAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentTestResults_PracticeTestTemplates_PracticeTestTemplateId",
                table: "StudentTestResults");

            migrationBuilder.DropTable(
                name: "PracticeTestTemplateQuestions");

            migrationBuilder.DropTable(
                name: "PracticeTestTemplates");

            migrationBuilder.DropIndex(
                name: "IX_StudentTestResults_PracticeTestTemplateId",
                table: "StudentTestResults");

            migrationBuilder.DropIndex(
                name: "IX_StudentTestResults_UserId_MockTestId_Status",
                table: "StudentTestResults");

            migrationBuilder.DropIndex(
                name: "IX_StudentAnswers_QuestionBankQuestionId",
                table: "StudentAnswers");

            migrationBuilder.DropIndex(
                name: "IX_StudentAnswers_StudentTestResultId_QuestionOrder",
                table: "StudentAnswers");

            migrationBuilder.DropIndex(
                name: "IX_MockTestQuestions_QuestionBankQuestionId",
                table: "MockTestQuestions");

            migrationBuilder.DropColumn(
                name: "NegativeMarkingRatio",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PracticeDifficulty",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PracticeDurationMinutes",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PracticeExamId",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PracticeSubjectId",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PracticeTestTemplateId",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PracticeTopicId",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PracticeYearFrom",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PracticeYearTo",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "TestKind",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "AnsweredAt",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "CorrectOptionSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "ExplanationSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "IsMarkedForReview",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "OptionASnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "OptionBSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "OptionCSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "OptionDSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "QuestionBankQuestionId",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "QuestionOrder",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "QuestionTextSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "SubjectSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "TopicSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "DifficultyLevel",
                table: "QuestionBankQuestions");

            migrationBuilder.DropColumn(
                name: "EndAt",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "QuestionBankQuestionId",
                table: "MockTestQuestions");

            migrationBuilder.AlterColumn<Guid>(
                name: "MockTestId",
                table: "StudentTestResults",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "StudentAnswers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "MockTestQuestions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentTestResults_UserId",
                table: "StudentTestResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_StudentTestResultId",
                table: "StudentAnswers",
                column: "StudentTestResultId");
        }
    }
}
