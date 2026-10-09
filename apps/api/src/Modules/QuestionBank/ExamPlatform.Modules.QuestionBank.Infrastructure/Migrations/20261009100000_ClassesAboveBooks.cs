using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClassesAboveBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Classes",
                schema: "questionBank",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classes", x => x.Id);
                });

            // Every book made before classes existed is under none, so existing books, chapters, questions and exams read as before.
            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                schema: "questionBank",
                table: "Books",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Classes_Name",
                schema: "questionBank",
                table: "Classes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_ClassId",
                schema: "questionBank",
                table: "Books",
                column: "ClassId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Classes_ClassId",
                schema: "questionBank",
                table: "Books",
                column: "ClassId",
                principalSchema: "questionBank",
                principalTable: "Classes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Classes_ClassId",
                schema: "questionBank",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_ClassId",
                schema: "questionBank",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ClassId",
                schema: "questionBank",
                table: "Books");

            migrationBuilder.DropTable(
                name: "Classes",
                schema: "questionBank");
        }
    }
}
