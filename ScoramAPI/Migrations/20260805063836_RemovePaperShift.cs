using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemovePaperShift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Papers_ExamId_Year_Shift_Language",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "Shift",
                table: "Papers");

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "QuestionSolutions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "QuestionSolutions",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Papers_ExamId_Year_Language",
                table: "Papers",
                columns: new[] { "ExamId", "Year", "Language" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Papers_ExamId_Year_Language",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "QuestionSolutions");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "QuestionSolutions");

            migrationBuilder.AddColumn<string>(
                name: "Shift",
                table: "Papers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Papers_ExamId_Year_Shift_Language",
                table: "Papers",
                columns: new[] { "ExamId", "Year", "Shift", "Language" });
        }
    }
}
