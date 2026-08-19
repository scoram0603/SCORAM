using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "QuestionSolutions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionBankQuestionId",
                table: "QuestionSolutions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "QuestionReports",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "QuestionBankQuestionId",
                table: "QuestionReports",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuestionBankImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ValidRows = table.Column<int>(type: "int", nullable: false),
                    InvalidRows = table.Column<int>(type: "int", nullable: false),
                    DuplicateRows = table.Column<int>(type: "int", nullable: false),
                    ImportedCount = table.Column<int>(type: "int", nullable: false),
                    MergedIntoExistingCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBankImportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBankImportJobs_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionBankSubjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBankSubjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBankSubjects_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionBankTopics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBankTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBankTopics_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionBankTopics_QuestionBankSubjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "QuestionBankSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionBankQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedQuestionText = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OptionA = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OptionB = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OptionC = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OptionD = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrectOption = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBankQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBankQuestions_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestionBankQuestions_QuestionBankImportJobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalTable: "QuestionBankImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestionBankQuestions_QuestionBankSubjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "QuestionBankSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestionBankQuestions_QuestionBankTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "QuestionBankTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionBankExamMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionBankQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBankExamMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBankExamMappings_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestionBankExamMappings_QuestionBankQuestions_QuestionBankQuestionId",
                        column: x => x.QuestionBankQuestionId,
                        principalTable: "QuestionBankQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSolutions_QuestionBankQuestionId",
                table: "QuestionSolutions",
                column: "QuestionBankQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReports_QuestionBankQuestionId",
                table: "QuestionReports",
                column: "QuestionBankQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankExamMappings_ExamId",
                table: "QuestionBankExamMappings",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankExamMappings_QuestionBankQuestionId_ExamId_Year",
                table: "QuestionBankExamMappings",
                columns: new[] { "QuestionBankQuestionId", "ExamId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankExamMappings_Year",
                table: "QuestionBankExamMappings",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankImportJobs_CreatedByAdminId",
                table: "QuestionBankImportJobs",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankQuestions_CreatedByAdminId",
                table: "QuestionBankQuestions",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankQuestions_ImportJobId",
                table: "QuestionBankQuestions",
                column: "ImportJobId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankQuestions_NormalizedQuestionText",
                table: "QuestionBankQuestions",
                column: "NormalizedQuestionText");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankQuestions_SubjectId_TopicId",
                table: "QuestionBankQuestions",
                columns: new[] { "SubjectId", "TopicId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankQuestions_TopicId",
                table: "QuestionBankQuestions",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankSubjects_CreatedByAdminId",
                table: "QuestionBankSubjects",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankSubjects_Name",
                table: "QuestionBankSubjects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankTopics_CreatedByAdminId",
                table: "QuestionBankTopics",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankTopics_SubjectId_Name",
                table: "QuestionBankTopics",
                columns: new[] { "SubjectId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionReports_QuestionBankQuestions_QuestionBankQuestionId",
                table: "QuestionReports",
                column: "QuestionBankQuestionId",
                principalTable: "QuestionBankQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionSolutions_QuestionBankQuestions_QuestionBankQuestionId",
                table: "QuestionSolutions",
                column: "QuestionBankQuestionId",
                principalTable: "QuestionBankQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestionReports_QuestionBankQuestions_QuestionBankQuestionId",
                table: "QuestionReports");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestionSolutions_QuestionBankQuestions_QuestionBankQuestionId",
                table: "QuestionSolutions");

            migrationBuilder.DropTable(
                name: "QuestionBankExamMappings");

            migrationBuilder.DropTable(
                name: "QuestionBankQuestions");

            migrationBuilder.DropTable(
                name: "QuestionBankImportJobs");

            migrationBuilder.DropTable(
                name: "QuestionBankTopics");

            migrationBuilder.DropTable(
                name: "QuestionBankSubjects");

            migrationBuilder.DropIndex(
                name: "IX_QuestionSolutions_QuestionBankQuestionId",
                table: "QuestionSolutions");

            migrationBuilder.DropIndex(
                name: "IX_QuestionReports_QuestionBankQuestionId",
                table: "QuestionReports");

            migrationBuilder.DropColumn(
                name: "QuestionBankQuestionId",
                table: "QuestionSolutions");

            migrationBuilder.DropColumn(
                name: "QuestionBankQuestionId",
                table: "QuestionReports");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "QuestionSolutions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "QuestionReports",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
