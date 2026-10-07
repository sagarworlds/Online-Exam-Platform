using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GrantQuestionReviewPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only: the schema does not change. Roles that can manage questions keep being able to read them after the read
            // permission was split out (FR-8), without waiting for the seeder or a code change.
            migrationBuilder.Sql(QuestionReviewPermissions.GrantSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM identity."RolePermissions"
                WHERE "PermissionsId" IN (SELECT "Id" FROM identity."Permissions" WHERE "Code" IN ('question.read', 'question.review'));
                DELETE FROM identity."Permissions" WHERE "Code" IN ('question.read', 'question.review');
                """);
        }
    }
}
