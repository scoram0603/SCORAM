using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // QUESTION BANK ("PYQs") BROWSING PROGRESS -- purely ADDITIVE: two new tables, nothing existing is
    // altered or dropped, so it is safe on production data.
    //   * QuestionBrowseSessions -- one ACTIVE row per (student, scope) (filtered unique index);
    //                               "Start fresh" archives the row (IsActive = 0), never deletes it.
    //   * QuestionBrowseAnswers  -- one row per (session, question): the option the student tapped.
    // ROLLBACK: `dotnet ef database update <previous migration>` runs Down(), which drops exactly these
    // two tables (answers first).
    //
    // NOTE: written out by hand (no .Designer.cs), same as AddAdminPlatformFeatures; the model snapshot
    // was updated by hand to match, so the next `dotnet ef migrations add` should come out empty for these.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261019000000_AddPyqBrowsingProgress")]
    public partial class AddPyqBrowsingProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionBrowseSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FiltersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LastQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastPosition = table.Column<int>(type: "int", nullable: false),
                    ViewMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PositionClientUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBrowseSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBrowseSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_QuestionBrowseSessions_ActivePerUser",
                table: "QuestionBrowseSessions",
                columns: new[] { "UserId", "Scope" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateTable(
                name: "QuestionBrowseAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionBankQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedOption = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    ClientUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBrowseAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionBrowseAnswers_QuestionBankQuestions_QuestionBankQuestionId",
                        column: x => x.QuestionBankQuestionId,
                        principalTable: "QuestionBankQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionBrowseAnswers_QuestionBrowseSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "QuestionBrowseSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBrowseAnswers_QuestionBankQuestionId",
                table: "QuestionBrowseAnswers",
                column: "QuestionBankQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBrowseAnswers_SessionId_QuestionBankQuestionId",
                table: "QuestionBrowseAnswers",
                columns: new[] { "SessionId", "QuestionBankQuestionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuestionBrowseAnswers");
            migrationBuilder.DropTable(name: "QuestionBrowseSessions");
        }
    }
}
