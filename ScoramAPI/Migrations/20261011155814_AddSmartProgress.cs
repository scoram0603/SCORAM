using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimeSpentSeconds",
                table: "StudentAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareStudyPlan",
                table: "PrivacySettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MistakeRevisionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionBankQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDismissed = table.Column<bool>(type: "bit", nullable: false),
                    RevisionAttemptCount = table.Column<int>(type: "int", nullable: false),
                    RevisionCorrectCount = table.Column<int>(type: "int", nullable: false),
                    ConsecutiveCorrect = table.Column<int>(type: "int", nullable: false),
                    LastRevisedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextDueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MistakeRevisionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MistakeRevisionItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RevisionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionBankQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SelectedOption = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "int", nullable: true),
                    SubjectSnapshot = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    TopicSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevisionAttempts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TaskKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetCount = table.Column<int>(type: "int", nullable: false),
                    ProgressCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubjectName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    TopicName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DismissedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyPlanTasks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MistakeRevisionItems_UserId_QuestionBankQuestionId",
                table: "MistakeRevisionItems",
                columns: new[] { "UserId", "QuestionBankQuestionId" },
                unique: true,
                filter: "[QuestionBankQuestionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MistakeRevisionItems_UserId_QuestionId",
                table: "MistakeRevisionItems",
                columns: new[] { "UserId", "QuestionId" },
                unique: true,
                filter: "[QuestionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionAttempts_UserId_AnsweredAt",
                table: "RevisionAttempts",
                columns: new[] { "UserId", "AnsweredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RevisionAttempts_UserId_ClientRequestId",
                table: "RevisionAttempts",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionAttempts_UserId_QuestionBankQuestionId",
                table: "RevisionAttempts",
                columns: new[] { "UserId", "QuestionBankQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RevisionAttempts_UserId_QuestionId",
                table: "RevisionAttempts",
                columns: new[] { "UserId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTasks_UserId_PlanDate_TaskKey",
                table: "StudyPlanTasks",
                columns: new[] { "UserId", "PlanDate", "TaskKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MistakeRevisionItems");

            migrationBuilder.DropTable(
                name: "RevisionAttempts");

            migrationBuilder.DropTable(
                name: "StudyPlanTasks");

            migrationBuilder.DropColumn(
                name: "TimeSpentSeconds",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "ShareStudyPlan",
                table: "PrivacySettings");
        }
    }
}
