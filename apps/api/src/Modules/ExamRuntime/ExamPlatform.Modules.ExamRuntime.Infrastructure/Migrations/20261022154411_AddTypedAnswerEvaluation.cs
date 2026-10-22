using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTypedAnswerEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingRevisionReason",
                schema: "examRuntime",
                table: "Attempts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvaluatedAtUtc",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvaluationFailures",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationLastError",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvaluationLeaseUntilUtc",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationModel",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvaluationNextAttemptAtUtc",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationReason",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvaluationRequestedAtUtc",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextVerdict",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswers_EvaluationRequestedAtUtc",
                schema: "examRuntime",
                table: "AttemptAnswers",
                column: "EvaluationRequestedAtUtc",
                filter: "\"EvaluationRequestedAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttemptAnswers_EvaluationRequestedAtUtc",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "PendingRevisionReason",
                schema: "examRuntime",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "EvaluatedAtUtc",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "EvaluationFailures",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "EvaluationLastError",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "EvaluationLeaseUntilUtc",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "EvaluationModel",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "EvaluationNextAttemptAtUtc",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "EvaluationReason",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "EvaluationRequestedAtUtc",
                schema: "examRuntime",
                table: "AttemptAnswers");

            migrationBuilder.DropColumn(
                name: "TextVerdict",
                schema: "examRuntime",
                table: "AttemptAnswers");
        }
    }
}
