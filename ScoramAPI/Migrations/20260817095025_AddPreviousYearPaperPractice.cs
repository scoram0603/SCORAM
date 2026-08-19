using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviousYearPaperPractice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaperId",
                table: "StudentTestResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Papers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NegativeMarkingRatio",
                table: "Papers",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredQuestionCount",
                table: "Papers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaperQuestionBankLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaperId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionBankQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionNumber = table.Column<int>(type: "int", nullable: false),
                    LinkedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperQuestionBankLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperQuestionBankLinks_Admins_LinkedByAdminId",
                        column: x => x.LinkedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperQuestionBankLinks_Papers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "Papers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaperQuestionBankLinks_QuestionBankQuestions_QuestionBankQuestionId",
                        column: x => x.QuestionBankQuestionId,
                        principalTable: "QuestionBankQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentTestResults_PaperId",
                table: "StudentTestResults",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentTestResults_UserId_PaperId_Status",
                table: "StudentTestResults",
                columns: new[] { "UserId", "PaperId", "Status" },
                unique: true,
                filter: "[PaperId] IS NOT NULL AND [Status] = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "IX_PaperQuestionBankLinks_LinkedByAdminId",
                table: "PaperQuestionBankLinks",
                column: "LinkedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperQuestionBankLinks_PaperId_QuestionBankQuestionId",
                table: "PaperQuestionBankLinks",
                columns: new[] { "PaperId", "QuestionBankQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperQuestionBankLinks_PaperId_QuestionNumber",
                table: "PaperQuestionBankLinks",
                columns: new[] { "PaperId", "QuestionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperQuestionBankLinks_QuestionBankQuestionId",
                table: "PaperQuestionBankLinks",
                column: "QuestionBankQuestionId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentTestResults_Papers_PaperId",
                table: "StudentTestResults",
                column: "PaperId",
                principalTable: "Papers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentTestResults_Papers_PaperId",
                table: "StudentTestResults");

            migrationBuilder.DropTable(
                name: "PaperQuestionBankLinks");

            migrationBuilder.DropIndex(
                name: "IX_StudentTestResults_PaperId",
                table: "StudentTestResults");

            migrationBuilder.DropIndex(
                name: "IX_StudentTestResults_UserId_PaperId_Status",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "PaperId",
                table: "StudentTestResults");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "NegativeMarkingRatio",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "RequiredQuestionCount",
                table: "Papers");
        }
    }
}
