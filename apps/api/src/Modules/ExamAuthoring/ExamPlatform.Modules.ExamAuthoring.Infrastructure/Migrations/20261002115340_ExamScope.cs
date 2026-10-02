using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExamScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScopeBookId",
                schema: "examAuthoring",
                table: "Exams",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "ScopeChapterIds",
                schema: "examAuthoring",
                table: "Exams",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                schema: "examAuthoring",
                table: "Exams",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every exam that exists before scopes is an independent one: it may draw from the whole bank.
                defaultValue: "Independent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScopeBookId",
                schema: "examAuthoring",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ScopeChapterIds",
                schema: "examAuthoring",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                schema: "examAuthoring",
                table: "Exams");
        }
    }
}
