using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// Maps OtpChallenges' Postgres <c>xmin</c> system column as the row's optimistic
    /// concurrency token. Up and Down are deliberately empty: every Postgres row already
    /// has <c>xmin</c>, so there is no schema to change (EF scaffolds an AddColumn that
    /// Npgsql would skip anyway). The migration exists to move the model snapshot
    /// forward, because EF Core 9+ refuses to migrate while the snapshot lags the model.
    /// </summary>
    public partial class OtpChallengeConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: xmin is a system column (see the class summary).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: xmin is a system column (see the class summary).
        }
    }
}
