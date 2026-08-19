using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class GroupChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Users_UserId",
                table: "ChatMessages");

            migrationBuilder.DeleteData(
                table: "ChatRooms",
                keyColumn: "Id",
                keyValue: new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000001"));

            migrationBuilder.DeleteData(
                table: "ChatRooms",
                keyColumn: "Id",
                keyValue: new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000002"));

            migrationBuilder.DeleteData(
                table: "ChatRooms",
                keyColumn: "Id",
                keyValue: new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000003"));

            migrationBuilder.DeleteData(
                table: "ChatRooms",
                keyColumn: "Id",
                keyValue: new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000004"));

            migrationBuilder.DeleteData(
                table: "ChatRooms",
                keyColumn: "Id",
                keyValue: new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000005"));

            migrationBuilder.DeleteData(
                table: "ChatRooms",
                keyColumn: "Id",
                keyValue: new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000006"));

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "ChatRooms",
                newName: "IsChatDisabled");

            migrationBuilder.AddColumn<Guid>(
                name: "ExamId",
                table: "ChatRooms",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "MessageText",
                table: "ChatMessages",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                table: "ChatMessages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PollId",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SenderAdminId",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId1",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BannedWords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Word = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannedWords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BannedWords_Admins_AddedByAdminId",
                        column: x => x.AddedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessageMentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MentionedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessageMentions_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessageMentions_Users_MentionedUserId",
                        column: x => x.MentionedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatPolls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AllowMultipleChoices = table.Column<bool>(type: "bit", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatPolls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatPolls_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatPolls_ChatRooms_ChatRoomId",
                        column: x => x.ChatRoomId,
                        principalTable: "ChatRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResolvedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatReports_Admins_ResolvedByAdminId",
                        column: x => x.ResolvedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatReports_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatReports_Users_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatRoomMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsBanned = table.Column<bool>(type: "bit", nullable: false),
                    BannedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BannedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRoomMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatRoomMemberships_Admins_BannedByAdminId",
                        column: x => x.BannedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatRoomMemberships_ChatRooms_ChatRoomId",
                        column: x => x.ChatRoomId,
                        principalTable: "ChatRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatRoomMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatPollOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatPollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatPollOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatPollOptions_ChatPolls_ChatPollId",
                        column: x => x.ChatPollId,
                        principalTable: "ChatPolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatPollVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatPollOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VotedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatPollVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatPollVotes_ChatPollOptions_ChatPollOptionId",
                        column: x => x.ChatPollOptionId,
                        principalTable: "ChatPollOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatPollVotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_ExamId",
                table: "ChatRooms",
                column: "ExamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_PollId",
                table: "ChatMessages",
                column: "PollId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SenderAdminId",
                table: "ChatMessages",
                column: "SenderAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_UserId1",
                table: "ChatMessages",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_BannedWords_AddedByAdminId",
                table: "BannedWords",
                column: "AddedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_BannedWords_Word",
                table: "BannedWords",
                column: "Word",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageMentions_ChatMessageId",
                table: "ChatMessageMentions",
                column: "ChatMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageMentions_MentionedUserId_IsRead",
                table: "ChatMessageMentions",
                columns: new[] { "MentionedUserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatPollOptions_ChatPollId",
                table: "ChatPollOptions",
                column: "ChatPollId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatPolls_ChatRoomId",
                table: "ChatPolls",
                column: "ChatRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatPolls_CreatedByAdminId",
                table: "ChatPolls",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatPollVotes_ChatPollOptionId_UserId",
                table: "ChatPollVotes",
                columns: new[] { "ChatPollOptionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatPollVotes_UserId",
                table: "ChatPollVotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatReports_ChatMessageId",
                table: "ChatReports",
                column: "ChatMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatReports_ReportedByUserId",
                table: "ChatReports",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatReports_ResolvedByAdminId",
                table: "ChatReports",
                column: "ResolvedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomMemberships_BannedByAdminId",
                table: "ChatRoomMemberships",
                column: "BannedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomMemberships_ChatRoomId_UserId",
                table: "ChatRoomMemberships",
                columns: new[] { "ChatRoomId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomMemberships_UserId",
                table: "ChatRoomMemberships",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Admins_SenderAdminId",
                table: "ChatMessages",
                column: "SenderAdminId",
                principalTable: "Admins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_ChatPolls_PollId",
                table: "ChatMessages",
                column: "PollId",
                principalTable: "ChatPolls",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Users_UserId",
                table: "ChatMessages",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Users_UserId1",
                table: "ChatMessages",
                column: "UserId1",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatRooms_Exams_ExamId",
                table: "ChatRooms",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Admins_SenderAdminId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_ChatPolls_PollId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Users_UserId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Users_UserId1",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatRooms_Exams_ExamId",
                table: "ChatRooms");

            migrationBuilder.DropTable(
                name: "BannedWords");

            migrationBuilder.DropTable(
                name: "ChatMessageMentions");

            migrationBuilder.DropTable(
                name: "ChatPollVotes");

            migrationBuilder.DropTable(
                name: "ChatReports");

            migrationBuilder.DropTable(
                name: "ChatRoomMemberships");

            migrationBuilder.DropTable(
                name: "ChatPollOptions");

            migrationBuilder.DropTable(
                name: "ChatPolls");

            migrationBuilder.DropIndex(
                name: "IX_ChatRooms_ExamId",
                table: "ChatRooms");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_PollId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_SenderAdminId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_UserId1",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ExamId",
                table: "ChatRooms");

            migrationBuilder.DropColumn(
                name: "MessageType",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "PollId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "SenderAdminId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "ChatMessages");

            migrationBuilder.RenameColumn(
                name: "IsChatDisabled",
                table: "ChatRooms",
                newName: "IsActive");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MessageText",
                table: "ChatMessages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.InsertData(
                table: "ChatRooms",
                columns: new[] { "Id", "CreatedAt", "Description", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000001"), new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Discussion room for SSC CGL aspirants", true, "SSC CGL" },
                    { new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000002"), new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Discussion room for SSC CHSL aspirants", true, "SSC CHSL" },
                    { new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000003"), new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Discussion room for Railway NTPC aspirants", true, "Railway NTPC" },
                    { new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000004"), new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Discussion room for Railway Group D aspirants", true, "Railway Group D" },
                    { new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000005"), new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Ask and resolve daily doubts", true, "Daily Doubt Room" },
                    { new Guid("3c9e0a1b-1111-4a2b-8c3d-000000000006"), new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Discuss daily current affairs", true, "Current Affairs Room" }
                });

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Users_UserId",
                table: "ChatMessages",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
