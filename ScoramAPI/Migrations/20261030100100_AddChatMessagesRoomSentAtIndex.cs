using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // ONE additive, non-clustered index: ChatMessages (ChatRoomId, SentAt DESC, Id DESC).
    //
    // Matches two confirmed queries (not speculative):
    //   * ChatController.GetMessages  -- WHERE ChatRoomId = @id [AND SentAt < @before] ORDER BY SentAt DESC, Id DESC TOP n
    //   * ChatController.ListRooms    -- per room: WHERE ChatRoomId = r.Id AND IsDeleted = 0 ORDER BY SentAt DESC TOP 1
    // Before this, ChatMessages only had a single-column index on ChatRoomId, so both had to read and sort every
    // message of the room. IsDeleted is intentionally NOT part of the index (only a residual filter on the first
    // rows read); add it only if Query Store/the execution plan shows deleted rows dominating.
    //
    // No column, row or existing index is touched, and the existing IX_ChatMessages_ChatRoomId is kept (it is now
    // redundant with this one's leading column and can be dropped later once Query Store confirms nothing needs
    // it). Cost: a small extra write on every chat insert and some storage. VALIDATE ON STAGING FIRST: compare the
    // actual plans / logical reads of the two queries before and after (see the performance runbook).
    //
    // Size: the table is a chat log; creating the index briefly takes a schema-modification lock. Apply off-peak.
    // Azure SQL can build it ONLINE -- if you apply the SQL script by hand use WITH (ONLINE = ON).
    //
    // Down drops only this index.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261030100100_AddChatMessagesRoomSentAtIndex")]
    public partial class AddChatMessagesRoomSentAtIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatRoomId_SentAt_Id",
                table: "ChatMessages",
                columns: new[] { "ChatRoomId", "SentAt", "Id" },
                descending: new[] { false, true, true });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ChatRoomId_SentAt_Id",
                table: "ChatMessages");
        }
    }
}
