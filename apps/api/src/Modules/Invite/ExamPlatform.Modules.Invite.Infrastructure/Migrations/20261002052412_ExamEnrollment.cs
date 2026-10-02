using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Invite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExamEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "invite",
                table: "Invites");

            migrationBuilder.AlterColumn<Guid>(
                name: "BatchMemberId",
                schema: "invite",
                table: "Invites",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "AcceptedByUserId",
                schema: "invite",
                table: "Invites",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invites_AcceptedByUserId_ExamId",
                schema: "invite",
                table: "Invites",
                columns: new[] { "AcceptedByUserId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_InviteCodes_Code",
                schema: "invite",
                table: "InviteCodes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invites_AcceptedByUserId_ExamId",
                schema: "invite",
                table: "Invites");

            migrationBuilder.DropIndex(
                name: "IX_InviteCodes_Code",
                schema: "invite",
                table: "InviteCodes");

            migrationBuilder.DropColumn(
                name: "AcceptedByUserId",
                schema: "invite",
                table: "Invites");

            migrationBuilder.AlterColumn<Guid>(
                name: "BatchMemberId",
                schema: "invite",
                table: "Invites",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedBy",
                schema: "invite",
                table: "Invites",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
