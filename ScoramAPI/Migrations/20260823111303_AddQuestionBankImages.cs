using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionBankImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExplanationImageUrl",
                table: "QuestionBankQuestions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionAImageUrl",
                table: "QuestionBankQuestions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionBImageUrl",
                table: "QuestionBankQuestions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionCImageUrl",
                table: "QuestionBankQuestions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionDImageUrl",
                table: "QuestionBankQuestions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionImageUrl",
                table: "QuestionBankQuestions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExplanationImageUrl",
                table: "QuestionBankQuestions");

            migrationBuilder.DropColumn(
                name: "OptionAImageUrl",
                table: "QuestionBankQuestions");

            migrationBuilder.DropColumn(
                name: "OptionBImageUrl",
                table: "QuestionBankQuestions");

            migrationBuilder.DropColumn(
                name: "OptionCImageUrl",
                table: "QuestionBankQuestions");

            migrationBuilder.DropColumn(
                name: "OptionDImageUrl",
                table: "QuestionBankQuestions");

            migrationBuilder.DropColumn(
                name: "QuestionImageUrl",
                table: "QuestionBankQuestions");
        }
    }
}
