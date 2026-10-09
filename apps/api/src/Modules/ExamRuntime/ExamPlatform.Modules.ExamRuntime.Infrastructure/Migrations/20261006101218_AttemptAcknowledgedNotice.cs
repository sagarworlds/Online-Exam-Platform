using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttemptAcknowledgedNotice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedNotice",
                schema: "examRuntime",
                table: "Attempts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedNotice",
                schema: "examRuntime",
                table: "Attempts");
        }
    }
}
