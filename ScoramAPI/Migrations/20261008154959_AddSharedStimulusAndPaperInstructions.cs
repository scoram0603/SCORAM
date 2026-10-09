using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedStimulusAndPaperInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaperInstructions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaperId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentBlocksJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperInstructions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperInstructions_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperInstructions_Admins_UpdatedByAdminId",
                        column: x => x.UpdatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperInstructions_Papers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "Papers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SharedStimuli",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ContentBlocksJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedStimuli", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedStimuli_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SharedStimuli_Admins_UpdatedByAdminId",
                        column: x => x.UpdatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperQuestionStimuli",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharedStimulusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaperQuestionBankLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperQuestionStimuli", x => x.Id);
                    table.CheckConstraint("CK_PaperQuestionStimuli_ExactlyOneTarget", "([QuestionId] IS NOT NULL AND [PaperQuestionBankLinkId] IS NULL) OR ([QuestionId] IS NULL AND [PaperQuestionBankLinkId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PaperQuestionStimuli_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperQuestionStimuli_PaperQuestionBankLinks_PaperQuestionBankLinkId",
                        column: x => x.PaperQuestionBankLinkId,
                        principalTable: "PaperQuestionBankLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperQuestionStimuli_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaperQuestionStimuli_SharedStimuli_SharedStimulusId",
                        column: x => x.SharedStimulusId,
                        principalTable: "SharedStimuli",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperInstructions_CreatedByAdminId",
                table: "PaperInstructions",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperInstructions_PaperId_DisplayOrder",
                table: "PaperInstructions",
                columns: new[] { "PaperId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperInstructions_UpdatedByAdminId",
                table: "PaperInstructions",
                column: "UpdatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperQuestionStimuli_CreatedByAdminId",
                table: "PaperQuestionStimuli",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperQuestionStimuli_SharedStimulusId",
                table: "PaperQuestionStimuli",
                column: "SharedStimulusId");

            migrationBuilder.CreateIndex(
                name: "UX_PaperQuestionStimuli_Link_Stimulus",
                table: "PaperQuestionStimuli",
                columns: new[] { "PaperQuestionBankLinkId", "SharedStimulusId" },
                unique: true,
                filter: "[PaperQuestionBankLinkId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PaperQuestionStimuli_Question_Stimulus",
                table: "PaperQuestionStimuli",
                columns: new[] { "QuestionId", "SharedStimulusId" },
                unique: true,
                filter: "[QuestionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SharedStimuli_CreatedByAdminId",
                table: "SharedStimuli",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedStimuli_Status",
                table: "SharedStimuli",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SharedStimuli_UpdatedByAdminId",
                table: "SharedStimuli",
                column: "UpdatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "UX_SharedStimuli_BusinessId",
                table: "SharedStimuli",
                column: "BusinessId",
                unique: true,
                filter: "[BusinessId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperInstructions");

            migrationBuilder.DropTable(
                name: "PaperQuestionStimuli");

            migrationBuilder.DropTable(
                name: "SharedStimuli");
        }
    }
}
