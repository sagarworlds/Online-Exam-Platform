using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Consent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IncidentLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Incidents",
                schema: "consent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AffectedData = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LoggedById = table.Column<Guid>(type: "uuid", nullable: false),
                    LoggedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EscalationDueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IncidentStatusChanges",
                schema: "consent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ChangedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentStatusChanges_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "consent",
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentStatusChanges_IncidentId",
                schema: "consent",
                table: "IncidentStatusChanges",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Status_EscalationDueAtUtc",
                schema: "consent",
                table: "Incidents",
                columns: new[] { "Status", "EscalationDueAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentStatusChanges",
                schema: "consent");

            migrationBuilder.DropTable(
                name: "Incidents",
                schema: "consent");
        }
    }
}
