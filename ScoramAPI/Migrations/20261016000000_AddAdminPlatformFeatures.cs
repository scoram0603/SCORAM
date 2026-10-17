using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // ADMIN PLATFORM EXPANSION -- everything below is ADDITIVE:
    //   * RefreshTokens.UserAgent            (nullable column -- session/device visibility)
    //   * UserFeedbacks.Priority / AssignedToAdminId / ResolvedAt (support workflow; existing rows get
    //     Priority='Normal', unassigned, not resolved)
    //   * FeedbackNotes                      (new table -- internal notes / responses / events)
    //   * AppSettings / AppSettingRevisions  (new tables -- maintenance mode, feature flags, app versions)
    // No existing column, row or index is altered or dropped, so it is safe on production data.
    //
    // ROLLBACK: `dotnet ef database update 20261011155814_AddSmartProgress` runs Down() below, which drops
    // exactly what Up() added (the three added columns, their FK/index, and the three new tables).
    //
    // NOTE: [DbContext]/[Migration] are written out here (no .Designer.cs), same as AddUserFeedback; the
    // model snapshot has been updated by hand to match so the next `dotnet ef migrations add` is clean.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261016000000_AddAdminPlatformFeatures")]
    public partial class AddAdminPlatformFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "RefreshTokens",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "UserFeedbacks",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToAdminId",
                table: "UserFeedbacks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "UserFeedbacks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedbacks_AssignedToAdminId",
                table: "UserFeedbacks",
                column: "AssignedToAdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserFeedbacks_Admins_AssignedToAdminId",
                table: "UserFeedbacks",
                column: "AssignedToAdminId",
                principalTable: "Admins",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateTable(
                name: "FeedbackNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeedbackId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackNotes_Admins_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackNotes_UserFeedbacks_FeedbackId",
                        column: x => x.FeedbackId,
                        principalTable: "UserFeedbacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackNotes_AdminId",
                table: "FeedbackNotes",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackNotes_FeedbackId_CreatedAt",
                table: "FeedbackNotes",
                columns: new[] { "FeedbackId", "CreatedAt" });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AppSettingRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettingRevisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettingRevisions_BatchId",
                table: "AppSettingRevisions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettingRevisions_Key_ChangedAt",
                table: "AppSettingRevisions",
                columns: new[] { "Key", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AppSettingRevisions");
            migrationBuilder.DropTable(name: "AppSettings");
            migrationBuilder.DropTable(name: "FeedbackNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_UserFeedbacks_Admins_AssignedToAdminId",
                table: "UserFeedbacks");
            migrationBuilder.DropIndex(
                name: "IX_UserFeedbacks_AssignedToAdminId",
                table: "UserFeedbacks");
            migrationBuilder.DropColumn(name: "ResolvedAt", table: "UserFeedbacks");
            migrationBuilder.DropColumn(name: "AssignedToAdminId", table: "UserFeedbacks");
            migrationBuilder.DropColumn(name: "Priority", table: "UserFeedbacks");

            migrationBuilder.DropColumn(name: "UserAgent", table: "RefreshTokens");
        }
    }
}
