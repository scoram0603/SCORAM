using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminTotpMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TotpSecret",
                table: "Admins",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TotpEnabled",
                table: "Admins",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TotpBackupCodeHashes",
                table: "Admins",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TotpSecret", table: "Admins");
            migrationBuilder.DropColumn(name: "TotpEnabled", table: "Admins");
            migrationBuilder.DropColumn(name: "TotpBackupCodeHashes", table: "Admins");
        }
    }
}
