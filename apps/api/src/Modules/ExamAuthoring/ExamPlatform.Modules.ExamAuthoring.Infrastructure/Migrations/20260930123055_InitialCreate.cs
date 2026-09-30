using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "examAuthoring");

            migrationBuilder.CreateTable(
                name: "Exams",
                schema: "examAuthoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Config_TotalTimeSeconds = table.Column<int>(type: "integer", nullable: true),
                    Config_ShuffleQuestions = table.Column<bool>(type: "boolean", nullable: false),
                    Config_ShuffleOptions = table.Column<bool>(type: "boolean", nullable: false),
                    Config_SectionLockEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Config_CalculatorAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    Config_ScratchpadAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    Config_MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    Config_MaxRetakes = table.Column<int>(type: "integer", nullable: false),
                    Config_ResultReleaseMode = table.Column<string>(type: "text", nullable: false),
                    Config_ResultReleaseTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Config_MarkingScheme_CorrectMarks = table.Column<decimal>(type: "numeric", nullable: false),
                    Config_MarkingScheme_IncorrectMarks = table.Column<decimal>(type: "numeric", nullable: false),
                    Config_MarkingScheme_UnattemptedMarks = table.Column<decimal>(type: "numeric", nullable: false),
                    ScheduledStartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledEndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LateEntryDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamSections",
                schema: "examAuthoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TimeSeconds = table.Column<int>(type: "integer", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSections_Exams_ExamId",
                        column: x => x.ExamId,
                        principalSchema: "examAuthoring",
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamQuestions",
                schema: "examAuthoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamQuestions_ExamSections_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "examAuthoring",
                        principalTable: "ExamSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_SectionId",
                schema: "examAuthoring",
                table: "ExamQuestions",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSections_ExamId",
                schema: "examAuthoring",
                table: "ExamSections",
                column: "ExamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamQuestions",
                schema: "examAuthoring");

            migrationBuilder.DropTable(
                name: "ExamSections",
                schema: "examAuthoring");

            migrationBuilder.DropTable(
                name: "Exams",
                schema: "examAuthoring");
        }
    }
}
