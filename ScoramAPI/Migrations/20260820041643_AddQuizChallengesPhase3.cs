using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizChallengesPhase3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuizChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengedAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizChallenges_StudentTestResults_ChallengedAttemptId",
                        column: x => x.ChallengedAttemptId,
                        principalTable: "StudentTestResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizChallenges_StudentTestResults_SourceAttemptId",
                        column: x => x.SourceAttemptId,
                        principalTable: "StudentTestResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizChallenges_Users_ChallengedUserId",
                        column: x => x.ChallengedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizChallenges_Users_ChallengerUserId",
                        column: x => x.ChallengerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizChallenges_ChallengedAttemptId",
                table: "QuizChallenges",
                column: "ChallengedAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizChallenges_ChallengedUserId_Status",
                table: "QuizChallenges",
                columns: new[] { "ChallengedUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizChallenges_ChallengerUserId",
                table: "QuizChallenges",
                column: "ChallengerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizChallenges_SourceAttemptId",
                table: "QuizChallenges",
                column: "SourceAttemptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizChallenges");
        }
    }
}
