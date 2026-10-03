using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultipleAnswerAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "SelectedOptionIds",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            // Every stored answer chose exactly one option; carry it over as a set of one before the old column goes.
            migrationBuilder.Sql(
                "UPDATE \"examRuntime\".\"AttemptAnswers\" SET \"SelectedOptionIds\" = ARRAY[\"SelectedOptionId\"];");

            migrationBuilder.DropColumn(
                name: "SelectedOptionId",
                schema: "examRuntime",
                table: "AttemptAnswers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SelectedOptionId",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // A set of several cannot be kept in one column; the first chosen option is what survives going back.
            migrationBuilder.Sql(
                "UPDATE \"examRuntime\".\"AttemptAnswers\" SET \"SelectedOptionId\" = \"SelectedOptionIds\"[1] WHERE cardinality(\"SelectedOptionIds\") > 0;");

            migrationBuilder.DropColumn(
                name: "SelectedOptionIds",
                schema: "examRuntime",
                table: "AttemptAnswers");
        }
    }
}
