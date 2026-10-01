using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // Business IDs (EXMSSC001, SUB001, TST0001, MCK0001, ADM0001): a human-readable identifier ADDED
    // alongside the existing GUID Id of Exams, QuestionBankSubjects (Subjects), PracticeTestTemplates
    // (Tests), MockTests and Admins.
    //
    // PURELY ADDITIVE -- safe on a production database:
    //   * Five nullable nvarchar(30) columns. No existing row is touched, no primary key or foreign
    //     key changes, nothing is dropped, truncated or recreated. Existing GUIDs are untouched.
    //   * Five unique indexes, FILTERED to "[BusinessId] IS NOT NULL". Every existing row is NULL right
    //     now, so a plain unique index would fail (SQL Server counts NULLs as equal); the filter makes
    //     the constraint apply from the first value onward and lets it be created before any data
    //     exists. The database itself then rejects a duplicate Business ID -- not just the app.
    //   * Two small bookkeeping tables: BusinessIdCounters (atomic "next number" per prefix) and
    //     BusinessIdRegistry (permanent ledger of every ID ever issued -- what makes "never reuse"
    //     hold even after a record is deleted or merged away).
    //
    // Existing rows receive their IDs from the app, not from this migration: on startup
    // BusinessIdService.RunStartupBackfillAsync numbers them deterministically (oldest first) using the
    // same code path new records use, so the organization-code rules for exams live in exactly one
    // place. See BUSINESS_IDS_REPORT.md.
    //
    // NOTE: the [DbContext]/[Migration] attributes are written out here (instead of living in a
    // separate .Designer.cs) for the same reason as AddQuestionBankSubjectUpdatedAt: EF only
    // discovers a migration class that carries them. The model snapshot has already been updated, so
    // the next `dotnet ef migrations add` won't try to re-add any of this.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20260930120000_AddBusinessIds")]
    public partial class AddBusinessIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessIdCounters",
                columns: table => new
                {
                    CounterKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessIdCounters", x => x.CounterKey);
                });

            migrationBuilder.CreateTable(
                name: "BusinessIdRegistry",
                columns: table => new
                {
                    BusinessId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessIdRegistry", x => x.BusinessId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessIdRegistry_EntityId",
                table: "BusinessIdRegistry",
                column: "EntityId");

            AddBusinessIdColumn(migrationBuilder, "Exams", "UX_Exams_BusinessId");
            AddBusinessIdColumn(migrationBuilder, "QuestionBankSubjects", "UX_QuestionBankSubjects_BusinessId");
            AddBusinessIdColumn(migrationBuilder, "PracticeTestTemplates", "UX_PracticeTestTemplates_BusinessId");
            AddBusinessIdColumn(migrationBuilder, "MockTests", "UX_MockTests_BusinessId");
            AddBusinessIdColumn(migrationBuilder, "Admins", "UX_Admins_BusinessId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropBusinessIdColumn(migrationBuilder, "Exams", "UX_Exams_BusinessId");
            DropBusinessIdColumn(migrationBuilder, "QuestionBankSubjects", "UX_QuestionBankSubjects_BusinessId");
            DropBusinessIdColumn(migrationBuilder, "PracticeTestTemplates", "UX_PracticeTestTemplates_BusinessId");
            DropBusinessIdColumn(migrationBuilder, "MockTests", "UX_MockTests_BusinessId");
            DropBusinessIdColumn(migrationBuilder, "Admins", "UX_Admins_BusinessId");

            migrationBuilder.DropTable(name: "BusinessIdRegistry");
            migrationBuilder.DropTable(name: "BusinessIdCounters");
        }

        private static void AddBusinessIdColumn(MigrationBuilder migrationBuilder, string table, string indexName)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessId",
                table: table,
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: indexName,
                table: table,
                column: "BusinessId",
                unique: true,
                filter: "[BusinessId] IS NOT NULL");
        }

        private static void DropBusinessIdColumn(MigrationBuilder migrationBuilder, string table, string indexName)
        {
            migrationBuilder.DropIndex(name: indexName, table: table);
            migrationBuilder.DropColumn(name: "BusinessId", table: table);
        }
    }
}
