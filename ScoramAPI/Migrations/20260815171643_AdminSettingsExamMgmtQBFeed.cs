using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AdminSettingsExamMgmtQBFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Exams",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SharedQuestionBankQuestionId",
                table: "DirectMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedQuestionExamName",
                table: "DirectMessages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IconUrl",
                table: "ChatRooms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PostPermission",
                table: "ChatRooms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessages_SharedQuestionBankQuestionId",
                table: "DirectMessages",
                column: "SharedQuestionBankQuestionId");

            migrationBuilder.AddForeignKey(
                name: "FK_DirectMessages_QuestionBankQuestions_SharedQuestionBankQuestionId",
                table: "DirectMessages",
                column: "SharedQuestionBankQuestionId",
                principalTable: "QuestionBankQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DirectMessages_QuestionBankQuestions_SharedQuestionBankQuestionId",
                table: "DirectMessages");

            migrationBuilder.DropIndex(
                name: "IX_DirectMessages_SharedQuestionBankQuestionId",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "SharedQuestionBankQuestionId",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "SharedQuestionExamName",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "IconUrl",
                table: "ChatRooms");

            migrationBuilder.DropColumn(
                name: "PostPermission",
                table: "ChatRooms");
        }
    }
}
