using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Migrations
{
    /// <summary>Adds the per-section draw rules that pick questions for each candidate when an attempt starts.</summary>
    public partial class SectionDrawRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SectionDrawRules",
                schema: "examAuthoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Topic = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SectionDrawRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SectionDrawRules_ExamSections_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "examAuthoring",
                        principalTable: "ExamSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SectionDrawRules_SectionId",
                schema: "examAuthoring",
                table: "SectionDrawRules",
                column: "SectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SectionDrawRules", schema: "examAuthoring");
        }
    }
}
