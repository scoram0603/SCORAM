using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuestionBankQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaperId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MockTestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookmarks_MockTests_MockTestId",
                        column: x => x.MockTestId,
                        principalTable: "MockTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Papers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "Papers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookmarks_QuestionBankQuestions_QuestionBankQuestionId",
                        column: x => x.QuestionBankQuestionId,
                        principalTable: "QuestionBankQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookmarks_QuestionComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "QuestionComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_CommentId",
                table: "Bookmarks",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_MockTestId",
                table: "Bookmarks",
                column: "MockTestId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_PaperId",
                table: "Bookmarks",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_QuestionBankQuestionId",
                table: "Bookmarks",
                column: "QuestionBankQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_QuestionId",
                table: "Bookmarks",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_CommentId",
                table: "Bookmarks",
                columns: new[] { "UserId", "CommentId" },
                unique: true,
                filter: "[CommentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_MockTestId",
                table: "Bookmarks",
                columns: new[] { "UserId", "MockTestId" },
                unique: true,
                filter: "[MockTestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_PaperId",
                table: "Bookmarks",
                columns: new[] { "UserId", "PaperId" },
                unique: true,
                filter: "[PaperId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_QuestionBankQuestionId",
                table: "Bookmarks",
                columns: new[] { "UserId", "QuestionBankQuestionId" },
                unique: true,
                filter: "[QuestionBankQuestionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_QuestionId",
                table: "Bookmarks",
                columns: new[] { "UserId", "QuestionId" },
                unique: true,
                filter: "[QuestionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookmarks");
        }
    }
}
