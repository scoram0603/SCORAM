using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // Exam-wise Subject & Topic Management: three NEW tables (ExamSubjects, ExamTopics,
    // QuestionBankExamClassifications). Purely additive -- no existing table or column is altered, no
    // existing row is read or rewritten, and nothing is backfilled here. Existing exams keep behaving
    // exactly as before until an admin runs "Initialise from current classification" for that exam
    // (a previewed + confirmed action in the admin panel). Down drops the three tables.
    //
    // The [DbContext]/[Migration] attributes are written out (no .Designer.cs) so EF discovers the class;
    // the model snapshot already contains the new entities so the next `migrations add` won't repeat them.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20261022120000_AddExamTaxonomy")]
    public partial class AddExamTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamSubjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSubjects", x => x.Id);
                    table.ForeignKey(name: "FK_ExamSubjects_Admins_CreatedByAdminId", column: x => x.CreatedByAdminId,
                        principalTable: "Admins", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExamSubjects_Exams_ExamId", column: x => x.ExamId,
                        principalTable: "Exams", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExamSubjects_QuestionBankSubjects_BaseSubjectId", column: x => x.BaseSubjectId,
                        principalTable: "QuestionBankSubjects", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamTopics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTopics", x => x.Id);
                    table.ForeignKey(name: "FK_ExamTopics_Admins_CreatedByAdminId", column: x => x.CreatedByAdminId,
                        principalTable: "Admins", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExamTopics_ExamSubjects_ExamSubjectId", column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExamTopics_QuestionBankTopics_BaseTopicId", column: x => x.BaseTopicId,
                        principalTable: "QuestionBankTopics", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionBankExamClassifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionBankQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionBankExamClassifications", x => x.Id);
                    table.ForeignKey(name: "FK_QuestionBankExamClassifications_Exams_ExamId", column: x => x.ExamId,
                        principalTable: "Exams", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_QuestionBankExamClassifications_ExamSubjects_ExamSubjectId", column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_QuestionBankExamClassifications_ExamTopics_ExamTopicId", column: x => x.ExamTopicId,
                        principalTable: "ExamTopics", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_QuestionBankExamClassifications_QuestionBankQuestions_QuestionBankQuestionId", column: x => x.QuestionBankQuestionId,
                        principalTable: "QuestionBankQuestions", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "UX_ExamSubjects_Exam_Name_Active", table: "ExamSubjects",
                columns: new[] { "ExamId", "Name" }, unique: true, filter: "[IsActive] = 1");
            migrationBuilder.CreateIndex(name: "UX_ExamSubjects_Exam_Base", table: "ExamSubjects",
                columns: new[] { "ExamId", "BaseSubjectId" }, unique: true, filter: "[BaseSubjectId] IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_ExamSubjects_BaseSubjectId", table: "ExamSubjects", column: "BaseSubjectId");
            migrationBuilder.CreateIndex(name: "IX_ExamSubjects_CreatedByAdminId", table: "ExamSubjects", column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(name: "UX_ExamTopics_Subject_Name_Active", table: "ExamTopics",
                columns: new[] { "ExamSubjectId", "Name" }, unique: true, filter: "[IsActive] = 1");
            migrationBuilder.CreateIndex(name: "IX_ExamTopics_BaseTopicId", table: "ExamTopics", column: "BaseTopicId");
            migrationBuilder.CreateIndex(name: "IX_ExamTopics_CreatedByAdminId", table: "ExamTopics", column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(name: "UX_QBExamClassifications_Question_Exam", table: "QuestionBankExamClassifications",
                columns: new[] { "QuestionBankQuestionId", "ExamId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_QuestionBankExamClassifications_ExamId_ExamSubjectId", table: "QuestionBankExamClassifications",
                columns: new[] { "ExamId", "ExamSubjectId" });
            migrationBuilder.CreateIndex(name: "IX_QuestionBankExamClassifications_ExamSubjectId", table: "QuestionBankExamClassifications", column: "ExamSubjectId");
            migrationBuilder.CreateIndex(name: "IX_QuestionBankExamClassifications_ExamTopicId", table: "QuestionBankExamClassifications", column: "ExamTopicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuestionBankExamClassifications");
            migrationBuilder.DropTable(name: "ExamTopics");
            migrationBuilder.DropTable(name: "ExamSubjects");
        }
    }
}
