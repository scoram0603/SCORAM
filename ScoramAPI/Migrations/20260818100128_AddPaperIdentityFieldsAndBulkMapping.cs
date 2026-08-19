using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoramAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperIdentityFieldsAndBulkMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExamDate",
                table: "Papers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaperLabel",
                table: "Papers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shift",
                table: "Papers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "Papers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsNumberExact",
                table: "PaperQuestionBankLinks",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExamDate",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "PaperLabel",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "Shift",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Papers");

            migrationBuilder.DropColumn(
                name: "IsNumberExact",
                table: "PaperQuestionBankLinks");
        }
    }
}
