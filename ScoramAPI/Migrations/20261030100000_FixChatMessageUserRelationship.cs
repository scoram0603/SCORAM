using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // PHANTOM RELATIONSHIP CLEAN-UP -- removes ChatMessages.UserId1 (+ IX_ChatMessages_UserId1 and
    // FK_ChatMessages_Users_UserId1).
    //
    // Why it existed: User.ChatMessages is a collection navigation, but the ChatMessage -> User mapping used
    // .WithMany() (no navigation), so EF modelled User.ChatMessages as a second, convention-discovered
    // relationship with a shadow FK called UserId1. ScoramDbContext now maps WithMany(u => u.ChatMessages), so
    // there is a single relationship (through UserId) and the shadow column is no longer part of the model.
    // Nothing in the application reads or writes User.ChatMessages, so no behaviour changes.
    //
    // SAFETY -- NOTHING IS DROPPED IF IT HOLDS DATA. UserId1 was never populated by application code, but the
    // check below proves it on the database being migrated: if any row has a non-NULL UserId1 the migration
    // THROWS (and, since a migration runs in a transaction, changes nothing) instead of dropping the column.
    // Still run the pre-check on staging/production first:
    //     SELECT COUNT(*) FROM dbo.ChatMessages WHERE UserId1 IS NOT NULL;      -- expected 0
    //
    // ORDER OF DEPLOYMENT: ship the code first (it no longer references UserId1, and the column being still
    // present is harmless), then apply this migration. Do NOT apply this migration while an older build that
    // still maps UserId1 is serving traffic -- that build's INSERTs name the column and would fail.
    //
    // Down re-creates the column, index and FK exactly as GroupChat created them (nullable, no cascade), so
    // rolling back the code and this migration together restores the previous schema. The re-created column is
    // empty (it only ever held NULLs).
    //
    // [DbContext]/[Migration] are written out (no .Designer.cs), as in AddExamTaxonomy; the model snapshot in
    // this change already reflects the post-migration model, so `migrations add` will not repeat it.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261030100000_FixChatMessageUserRelationship")]
    public partial class FixChatMessageUserRelationship : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dynamic SQL so the batch still compiles on a database where the column is already gone.
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.ChatMessages', 'UserId1') IS NOT NULL
BEGIN
    DECLARE @nonNull int;
    EXEC sp_executesql
        N'SELECT @n = COUNT(*) FROM dbo.ChatMessages WHERE UserId1 IS NOT NULL',
        N'@n int OUTPUT',
        @n = @nonNull OUTPUT;
    IF @nonNull > 0
        THROW 50001, 'FixChatMessageUserRelationship aborted: dbo.ChatMessages.UserId1 contains non-NULL values, so it was NOT dropped. Investigate before retrying.', 1;
END");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Users_UserId1",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_UserId1",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "ChatMessages");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId1",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_UserId1",
                table: "ChatMessages",
                column: "UserId1");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Users_UserId1",
                table: "ChatMessages",
                column: "UserId1",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
