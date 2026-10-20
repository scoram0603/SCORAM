using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // STUDENT REVIEWS + LANDING CONTENT -- purely ADDITIVE: two new tables, nothing existing is altered or
    // dropped, so it is safe on production data.
    //   * StudentReviews       -- one moderated public review per student (unique UserId; cascade on account delete).
    //   * LandingContentItems  -- admin-managed landing media references + Smart Progress copy (key/value).
    // ROLLBACK: `dotnet ef database update 20261019000000_AddPyqBrowsingProgress` runs Down(), which drops exactly
    // these two tables. The new admin permissions (ModerateReviews, ManageLandingContent) are stored by NAME in the
    // existing AdminPermissionGrants.Permission column (nvarchar(30)), so they need no schema change.
    //
    // NOTE: written out by hand (no .Designer.cs), same as AddAdminPlatformFeatures / AddPyqBrowsingProgress; the
    // model snapshot was updated by hand to match, so the next `dotnet ef migrations add` should come out empty
    // for these tables.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261019000100_AddStudentReviewsAndLandingContent")]
    public partial class AddStudentReviewsAndLandingContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LandingContentItems",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    MediaUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandingContentItems", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "StudentReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ShowNamePublicly = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModeratedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModeratedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModerationNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentReviews_UserId",
                table: "StudentReviews",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentReviews_Status_UpdatedAt",
                table: "StudentReviews",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LandingContentItems");
            migrationBuilder.DropTable(name: "StudentReviews");
        }
    }
}
