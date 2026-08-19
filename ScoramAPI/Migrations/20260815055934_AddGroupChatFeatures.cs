using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupChatFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatRooms_ExamId",
                table: "ChatRooms");

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamId",
                table: "ChatRooms",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                table: "ChatRooms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SharedQuestionBankQuestionId",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedQuestionExamName",
                table: "ChatMessages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_ExamId",
                table: "ChatRooms",
                column: "ExamId",
                unique: true,
                filter: "[ExamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SharedQuestionBankQuestionId",
                table: "ChatMessages",
                column: "SharedQuestionBankQuestionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_QuestionBankQuestions_SharedQuestionBankQuestionId",
                table: "ChatMessages",
                column: "SharedQuestionBankQuestionId",
                principalTable: "QuestionBankQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_QuestionBankQuestions_SharedQuestionBankQuestionId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatRooms_ExamId",
                table: "ChatRooms");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_SharedQuestionBankQuestionId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "IsFeatured",
                table: "ChatRooms");

            migrationBuilder.DropColumn(
                name: "SharedQuestionBankQuestionId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "SharedQuestionExamName",
                table: "ChatMessages");

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamId",
                table: "ChatRooms",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_ExamId",
                table: "ChatRooms",
                column: "ExamId",
                unique: true);
        }
    }
}
