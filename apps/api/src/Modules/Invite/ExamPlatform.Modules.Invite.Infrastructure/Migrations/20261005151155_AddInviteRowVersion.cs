using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Invite.Infrastructure.Migrations
{
    /// <summary>
    /// Maps <c>Invites.xmin</c> as the invite's concurrency token. Every Postgres table already has the
    /// <c>xmin</c> system column, so nothing is created; this migration only brings the model snapshot in line.
    /// </summary>
    public partial class AddInviteRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
