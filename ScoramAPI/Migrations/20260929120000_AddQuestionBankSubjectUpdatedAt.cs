using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScoramAPI.Data;

#nullable disable

namespace ScoramAPI.Migrations
{
    // Subject Management: adds QuestionBankSubjects.UpdatedAt (nullable datetime2) so the admin
    // Subjects list can show "last updated". Purely additive and nullable -- no existing row is
    // touched, no data is moved or rewritten, and rolling back (Down) just drops the column.
    //
    // NOTE: the [DbContext]/[Migration] attributes are written out here (instead of living in a
    // separate .Designer.cs the way `dotnet ef migrations add` generates them) because EF only
    // discovers a migration class that carries them. The model snapshot has already been updated
    // to include UpdatedAt, so the next `dotnet ef migrations add` won't try to re-add it.
    [DbContext(typeof(ScoramDbContext))]
    [Migration("20260929120000_AddQuestionBankSubjectUpdatedAt")]
    public partial class AddQuestionBankSubjectUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "QuestionBankSubjects",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "QuestionBankSubjects");
        }
    }
}
