using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamPlatform.Modules.Proctoring.Infrastructure;

/// <summary>
/// Recognises the one database conflict two overlapping scans of the same exam can produce (FR-27): both try to write an assessment for the
/// same attempt, and the unique index on that attempt refuses the second writer.
/// </summary>
/// <remarks>
/// Matching on the index name, not just the error code, keeps any other unique violation a real error (a 500), not a reason to tell a
/// reviewer to try again. The index is unique across the whole database, so the conflict is caught across web replicas too; an
/// in-process lock would not be.
/// </remarks>
public static class OverlappingScanDetector
{
    /// <summary>The unique index on an attempt's assessment, created by the module's initial migration.</summary>
    public const string AttemptIndexName = "IX_RiskAssessments_AttemptId";

    /// <summary>Postgres's error code for a unique violation.</summary>
    public const string UniqueViolationSqlState = "23505";

    /// <summary>Whether a save failed because another scan wrote the same attempt's assessment first.</summary>
    /// <param name="error">The error the save raised.</param>
    /// <returns>True only for a unique violation on the attempt index.</returns>
    public static bool IsOverlappingScan(Exception error) =>
        error is DbUpdateException { InnerException: PostgresException postgres }
        && IsAttemptIndexViolation(postgres.SqlState, postgres.ConstraintName);

    /// <summary>The rule itself, on the two fields of the database error: the code, and the name of the index that refused the row.</summary>
    /// <param name="sqlState">The Postgres error code.</param>
    /// <param name="constraintName">The name of the constraint or index that was violated.</param>
    /// <returns>True when the code is a unique violation and the index is the attempt index.</returns>
    public static bool IsAttemptIndexViolation(string? sqlState, string? constraintName) =>
        sqlState == UniqueViolationSqlState && constraintName == AttemptIndexName;
}
