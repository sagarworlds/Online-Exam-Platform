using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Invite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInviteCodeDirectSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "invite",
                table: "InviteCodes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AddColumn<bool>(
                name: "AllowsDirectSignIn",
                schema: "invite",
                table: "InviteCodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowsDirectSignIn",
                schema: "invite",
                table: "InviteCodes");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "invite",
                table: "InviteCodes",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }
    }
}
