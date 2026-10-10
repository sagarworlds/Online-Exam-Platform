using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EncryptQuestionContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Question content is held as text from now on (#57). A Postgres array or jsonb value cannot be cast to text implicitly, and a
            // Postgres array literal would not be the JSON the entities read, so the answers and options are converted to JSON text first.
            // The content backfill then encrypts those values in place, which is why it runs straight after this migration.
            migrationBuilder.Sql(
                "ALTER TABLE \"questionBank\".\"Questions\" ALTER COLUMN \"AcceptedAnswers\" TYPE text " +
                "USING COALESCE(array_to_json(\"AcceptedAnswers\")::text, '[]');");
            migrationBuilder.Sql(
                "ALTER TABLE \"questionBank\".\"QuestionVersions\" ALTER COLUMN \"AcceptedAnswers\" TYPE text " +
                "USING COALESCE(array_to_json(\"AcceptedAnswers\")::text, '[]');");
            migrationBuilder.Sql(
                "ALTER TABLE \"questionBank\".\"QuestionVersions\" ALTER COLUMN \"Options\" TYPE text USING \"Options\"::text;");

            migrationBuilder.AlterColumn<string>(
                name: "AcceptedAnswers",
                schema: "questionBank",
                table: "Questions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string[]),
                oldType: "text[]");

            migrationBuilder.AlterColumn<string>(
                name: "Options",
                schema: "questionBank",
                table: "QuestionVersions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "AcceptedAnswers",
                schema: "questionBank",
                table: "QuestionVersions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string[]),
                oldType: "text[]");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                schema: "questionBank",
                table: "QuestionOptions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "questionBank",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });
        }

        /// <inheritdoc />
        /// <remarks>
        /// This does not decrypt the content. Rolling back would leave ciphertext in columns that were plaintext, so a rollback is only
        /// safe from a backup taken before this migration.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "questionBank");

            migrationBuilder.AlterColumn<string[]>(
                name: "AcceptedAnswers",
                schema: "questionBank",
                table: "Questions",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Options",
                schema: "questionBank",
                table: "QuestionVersions",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string[]>(
                name: "AcceptedAnswers",
                schema: "questionBank",
                table: "QuestionVersions",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                schema: "questionBank",
                table: "QuestionOptions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
