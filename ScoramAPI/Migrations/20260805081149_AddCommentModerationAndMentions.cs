using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentModerationAndMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestionComments_Users_UserId",
                table: "QuestionComments");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "QuestionComments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<int>(
                name: "DownvoteCount",
                table: "QuestionComments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsResolved",
                table: "QuestionComments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByAdminId",
                table: "QuestionComments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommentReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentReports_QuestionComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "QuestionComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommentReports_Users_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionComments_SubmittedByAdminId",
                table: "QuestionComments",
                column: "SubmittedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentReports_CommentId",
                table: "CommentReports",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentReports_ReportedByUserId",
                table: "CommentReports",
                column: "ReportedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionComments_Admins_SubmittedByAdminId",
                table: "QuestionComments",
                column: "SubmittedByAdminId",
                principalTable: "Admins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionComments_Users_UserId",
                table: "QuestionComments",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestionComments_Admins_SubmittedByAdminId",
                table: "QuestionComments");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestionComments_Users_UserId",
                table: "QuestionComments");

            migrationBuilder.DropTable(
                name: "CommentReports");

            migrationBuilder.DropIndex(
                name: "IX_QuestionComments_SubmittedByAdminId",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "DownvoteCount",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "IsResolved",
                table: "QuestionComments");

            migrationBuilder.DropColumn(
                name: "SubmittedByAdminId",
                table: "QuestionComments");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "QuestionComments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionComments_Users_UserId",
                table: "QuestionComments",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
