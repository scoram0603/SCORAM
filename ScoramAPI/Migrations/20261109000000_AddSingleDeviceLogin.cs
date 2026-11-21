using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    // SINGLE-DEVICE LOGIN -- adds the two columns the feature needs, both nullable with no default
    // and no backfill, so this is additive-only and safe to run against production data with
    // students actively logged in:
    //
    //   * Users.ActiveSessionId     -- null for every existing row until that student's next login
    //     after this migration runs. See User.ActiveSessionId's own comment, and
    //     AuthController.IssueAuthResponseAsync, for why a null value here (and the resulting
    //     absence of a "sid" claim on that student's current access token) means "not enforced yet"
    //     rather than an error -- this is exactly what keeps existing sessions from being logged out
    //     by this deployment.
    //   * RefreshTokens.RevokedReason -- null for every existing row (and for every future row
    //     revoked by any of the existing paths: rotation, Logout, ChangePassword, ResetPassword, the
    //     theft-reuse response in AuthController.Refresh). Only
    //     RefreshTokenService.SupersedeForNewLoginAsync ever sets it, to
    //     RefreshToken.RevokedReasonSupersededByLogin.
    //
    // No existing column, row, index, or table is altered or dropped.
    //
    // ROLLBACK: `dotnet ef database update 20261031054458_ProbeDrift` runs Down() below, which drops
    // exactly the two columns this migration added.
    /// <inheritdoc />
    public partial class AddSingleDeviceLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveSessionId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedReason",
                table: "RefreshTokens",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveSessionId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RevokedReason",
                table: "RefreshTokens");
        }
    }
}
