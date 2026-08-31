using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUserExamPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentBlocksJsonSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExplanationImageUrlSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionAImageUrlSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionBImageUrlSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionCImageUrlSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionDImageUrlSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionImageUrlSnapshot",
                table: "StudentAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExamId",
                table: "MockTests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserExamPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserExamPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserExamPreferences_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserExamPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockTests_ExamId",
                table: "MockTests",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_UserExamPreferences_ExamId",
                table: "UserExamPreferences",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_UserExamPreferences_UserId",
                table: "UserExamPreferences",
                column: "UserId",
                unique: true,
                filter: "[IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_UserExamPreferences_UserId_ExamId",
                table: "UserExamPreferences",
                columns: new[] { "UserId", "ExamId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MockTests_Exams_ExamId",
                table: "MockTests",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MockTests_Exams_ExamId",
                table: "MockTests");

            migrationBuilder.DropTable(
                name: "UserExamPreferences");

            migrationBuilder.DropIndex(
                name: "IX_MockTests_ExamId",
                table: "MockTests");

            migrationBuilder.DropColumn(
                name: "ContentBlocksJsonSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "ExplanationImageUrlSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "OptionAImageUrlSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "OptionBImageUrlSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "OptionCImageUrlSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "OptionDImageUrlSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "QuestionImageUrlSnapshot",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "ExamId",
                table: "MockTests");
        }
    }
}
