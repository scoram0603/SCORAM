using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // USER FEEDBACK -- the "UserFeedbacks" table behind the floating Feedback button (web + Flutter)
    // and Admin > Feedback.
    //
    // PURELY ADDITIVE -- creates one new table; touches no existing table, row or column, so it is
    // safe on a production database. In particular this migration does NOT touch UserExamPreferences:
    // every student's existing "Preparing For" / My Exams selection stays exactly as saved, so nobody
    // has to choose again (the table was already called My Exams internally).
    //
    // NOTE: the [DbContext]/[Migration] attributes are written out here (instead of living in a
    // separate .Designer.cs), same as AddBusinessIds: EF only discovers a migration class that
    // carries them. The model snapshot has already been updated, so the next
    // `dotnet ef migrations add` won't try to re-add any of this.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261002120000_AddUserFeedback")]
    public partial class AddUserFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeedbackNumber = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeedbackType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFeedbacks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedbacks_FeedbackNumber",
                table: "UserFeedbacks",
                column: "FeedbackNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedbacks_UserId",
                table: "UserFeedbacks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedbacks_Status_CreatedAt",
                table: "UserFeedbacks",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserFeedbacks");
        }
    }
}
