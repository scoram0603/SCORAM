using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class FixMissingSecurityStampRefreshTokensAndMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SecurityStamp",
                table: "Users",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Admins",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityStamp",
                table: "Admins",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "TotpBackupCodeHashes",
                table: "Admins",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TotpEnabled",
                table: "Admins",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecret",
                table: "Admins",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsAdmin = table.Column<bool>(type: "bit", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByIp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSolutions_IsApproved_CreatedAt",
                table: "QuestionSolutions",
                columns: new[] { "IsApproved", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_CreatedAt",
                table: "Questions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_DifficultyLevel",
                table: "Questions",
                column: "DifficultyLevel");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ExamName",
                table: "Questions",
                column: "ExamName");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_MirroredToQuestionBankQuestionId",
                table: "Questions",
                column: "MirroredToQuestionBankQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Subject",
                table: "Questions",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Topic",
                table: "Questions",
                column: "Topic");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Year",
                table: "Questions",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankQuestions_IsActive_CreatedAt",
                table: "QuestionBankQuestions",
                columns: new[] { "IsActive", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBankQuestions_Language",
                table: "QuestionBankQuestions",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_Papers_Status",
                table: "Papers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_PrincipalId_IsAdmin",
                table: "RefreshTokens",
                columns: new[] { "PrincipalId", "IsAdmin" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_QuestionSolutions_IsApproved_CreatedAt",
                table: "QuestionSolutions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_CreatedAt",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_DifficultyLevel",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_ExamName",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_MirroredToQuestionBankQuestionId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_Subject",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_Topic",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_Year",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_QuestionBankQuestions_IsActive_CreatedAt",
                table: "QuestionBankQuestions");

            migrationBuilder.DropIndex(
                name: "IX_QuestionBankQuestions_Language",
                table: "QuestionBankQuestions");

            migrationBuilder.DropIndex(
                name: "IX_Papers_Status",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "TotpBackupCodeHashes",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "TotpEnabled",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "TotpSecret",
                table: "Admins");
        }
    }
}
