using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // PUSH NOTIFICATION SYSTEM (FCM) -- extends the EXISTING Notifications and DeviceTokens tables; no
    // new table, no existing user/notification row removed.
    //
    //  Notifications : + EntityType, EntityId (structured navigation target), ReadAt, DedupKey
    //                  (idempotency), Type widened nvarchar(20) -> nvarchar(40) so the longer new
    //                  type names fit ("StudyPartnerAccepted" is already exactly 20).
    //  DeviceTokens  : + AppVersion, IsActive, LastUsedAt, UpdatedAt; Token nvarchar(max) ->
    //                  nvarchar(450) with a UNIQUE index (it previously had none, so concurrent
    //                  registrations could create duplicate rows); Platform -> nvarchar(20).
    //
    // DATA SAFETY: the only rows touched are exact-duplicate DeviceTokens rows (same Token), of which
    // the NEWEST is kept -- required, otherwise the unique index cannot be created. A token longer
    // than 450 chars (FCM tokens are ~160-260) would make the AlterColumn fail loudly rather than be
    // silently truncated or deleted; none are expected.
    //
    // Written by hand with the [DbContext]/[Migration] attributes inline (same as AddUserFeedback /
    // AddBusinessIds); the model snapshot is updated to match, so the next `dotnet ef migrations add`
    // will not re-add any of this. APPLY IT FIRST ON A LOCAL/STAGING COPY: this file could not be
    // compiled or run in the sandbox it was written in.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261003120000_AddPushNotificationMetadata")]
    public partial class AddPushNotificationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Notifications ----
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Notifications",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "EntityType", table: "Notifications", type: "nvarchar(50)", maxLength: 50, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "EntityId", table: "Notifications", type: "nvarchar(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt", table: "Notifications", type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "DedupKey", table: "Notifications", type: "nvarchar(150)", maxLength: 150, nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_DedupKey",
                table: "Notifications",
                columns: new[] { "UserId", "DedupKey" },
                unique: true,
                filter: "[DedupKey] IS NOT NULL");

            // ---- DeviceTokens ----
            // Collapse exact-duplicate tokens first (keep the newest row), or the unique index below fails.
            migrationBuilder.Sql(@"
                WITH Ranked AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY Token ORDER BY CreatedAt DESC, Id DESC) AS rn
                    FROM DeviceTokens
                )
                DELETE FROM Ranked WHERE rn > 1;");

            migrationBuilder.AlterColumn<string>(
                name: "Token", table: "DeviceTokens", type: "nvarchar(450)", maxLength: 450, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(max)");
            migrationBuilder.AlterColumn<string>(
                name: "Platform", table: "DeviceTokens", type: "nvarchar(20)", maxLength: 20, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "AppVersion", table: "DeviceTokens", type: "nvarchar(30)", maxLength: 30, nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "IsActive", table: "DeviceTokens", type: "bit", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt", table: "DeviceTokens", type: "datetime2", nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt", table: "DeviceTokens", type: "datetime2", nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_Token", table: "DeviceTokens", column: "Token", unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_UserId_IsActive", table: "DeviceTokens", columns: new[] { "UserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_DeviceTokens_UserId_IsActive", table: "DeviceTokens");
            migrationBuilder.DropIndex(name: "IX_DeviceTokens_Token", table: "DeviceTokens");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "DeviceTokens");
            migrationBuilder.DropColumn(name: "LastUsedAt", table: "DeviceTokens");
            migrationBuilder.DropColumn(name: "IsActive", table: "DeviceTokens");
            migrationBuilder.DropColumn(name: "AppVersion", table: "DeviceTokens");
            migrationBuilder.AlterColumn<string>(
                name: "Platform", table: "DeviceTokens", type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(20)", oldMaxLength: 20);
            migrationBuilder.AlterColumn<string>(
                name: "Token", table: "DeviceTokens", type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(450)", oldMaxLength: 450);

            migrationBuilder.DropIndex(name: "IX_Notifications_UserId_DedupKey", table: "Notifications");
            migrationBuilder.DropColumn(name: "DedupKey", table: "Notifications");
            migrationBuilder.DropColumn(name: "ReadAt", table: "Notifications");
            migrationBuilder.DropColumn(name: "EntityId", table: "Notifications");
            migrationBuilder.DropColumn(name: "EntityType", table: "Notifications");
            // NOTE: narrowing Type back to nvarchar(20) fails if any new-type row longer than 20 chars
            // (e.g. "SystemAnnouncement" is 18, fine; none currently exceed 20) exists.
            migrationBuilder.AlterColumn<string>(
                name: "Type", table: "Notifications", type: "nvarchar(20)", maxLength: 20, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(40)", oldMaxLength: 40);
        }
    }
}
