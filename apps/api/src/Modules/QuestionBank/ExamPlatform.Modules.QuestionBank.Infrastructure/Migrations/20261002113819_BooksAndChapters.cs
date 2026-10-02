using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BooksAndChapters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChapterId",
                schema: "questionBank",
                table: "Questions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Books",
                schema: "questionBank",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Chapters",
                schema: "questionBank",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chapters_Books_BookId",
                        column: x => x.BookId,
                        principalSchema: "questionBank",
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ChapterId",
                schema: "questionBank",
                table: "Questions",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_Name",
                schema: "questionBank",
                table: "Books",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_BookId_Order",
                schema: "questionBank",
                table: "Chapters",
                columns: new[] { "BookId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_BookId_Title",
                schema: "questionBank",
                table: "Chapters",
                columns: new[] { "BookId", "Title" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Chapters_ChapterId",
                schema: "questionBank",
                table: "Questions",
                column: "ChapterId",
                principalSchema: "questionBank",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Chapters_ChapterId",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "Chapters",
                schema: "questionBank");

            migrationBuilder.DropTable(
                name: "Books",
                schema: "questionBank");

            migrationBuilder.DropIndex(
                name: "IX_Questions_ChapterId",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "ChapterId",
                schema: "questionBank",
                table: "Questions");
        }
    }
}
