using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDefaultSuperAdminSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Admins",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The old HasData seed (removed from ScoramDbContext in this change) always created a
            // well-known SuperAdmin row with a well-known default password hash. Flag the row to force
            // a password change on next login, but ONLY if the PasswordHash still matches the original
            // seeded value exactly, so a deployment that already rotated the account's password is
            // never touched.
            //
            // Deliberately NOT a DeleteData op against this Id: deleting unconditionally would risk
            // destroying an already-secured account, and removing SuperAdmin login entirely was
            // explicitly out of scope for this fix -- forcing a change on next login is what "Do NOT
            // remove SuperAdmin login" plus "never leave the default password usable" both allow.
            migrationBuilder.Sql(@"
                UPDATE Admins
                SET MustChangePassword = 1
                WHERE Id = 'a1b2c3d4-0000-4000-8000-000000000001'
                  AND PasswordHash = '$2b$10$iHMto/L2wJaon4hjWIC8CeZNXGiQ3Fe4wMpa8tGvi9jybrHnSPqHa';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Admins");
        }
    }
}
