using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Batch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueActiveBatchMemberEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before the unique index can exist, existing rows must satisfy it. Addresses were never
            // normalized and members were never loaded when checking for duplicates, so a database may
            // hold the same person twice: fold the case, then retire every active duplicate but the
            // earliest seat (ties broken on Id).
            migrationBuilder.Sql(@"UPDATE batch.""BatchMembers"" SET ""Email"" = lower(btrim(""Email""));");
            migrationBuilder.Sql(@"
UPDATE batch.""BatchMembers"" m
SET ""IsDeleted"" = true
WHERE m.""IsDeleted"" = false
  AND EXISTS (
    SELECT 1 FROM batch.""BatchMembers"" earlier
    WHERE earlier.""BatchId"" = m.""BatchId""
      AND earlier.""Email"" = m.""Email""
      AND earlier.""IsDeleted"" = false
      AND (earlier.""CreatedAt"", earlier.""Id"") < (m.""CreatedAt"", m.""Id""));");

            migrationBuilder.DropIndex(
                name: "IX_BatchMembers_BatchId",
                schema: "batch",
                table: "BatchMembers");

            migrationBuilder.CreateIndex(
                name: "IX_BatchMembers_BatchId_Email",
                schema: "batch",
                table: "BatchMembers",
                columns: new[] { "BatchId", "Email" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BatchMembers_BatchId_Email",
                schema: "batch",
                table: "BatchMembers");

            migrationBuilder.CreateIndex(
                name: "IX_BatchMembers_BatchId",
                schema: "batch",
                table: "BatchMembers",
                column: "BatchId");
        }
    }
}
