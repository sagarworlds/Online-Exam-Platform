using ExamPlatform.Modules.Batch.Domain;

namespace ExamPlatform.Modules.Batch.Application.Dtos;

/// <summary>One roster row that was given a seat.</summary>
/// <param name="Row">The row's number in the file, counting a header as row 1, so it can be found again.</param>
/// <param name="MemberId">The new member's id.</param>
public sealed record RosterAddedRowDto(int Row, Guid MemberId);

/// <summary>One roster row that was not given a seat, and why. It names the row by number only, never by address (NFR-6).</summary>
/// <param name="Row">The row's number in the file, counting a header as row 1.</param>
/// <param name="Code">A stable name for the reason, for a client to translate: <c>missing_email</c>, <c>invalid_email</c>,
/// <c>invalid_phone</c>, <c>already_in_batch</c>, <c>repeated_in_file</c> or <c>batch_full</c>.</param>
/// <param name="Message">The reason in English, safe to show to whoever sent the file.</param>
public sealed record RosterSkippedRowDto(int Row, string Code, string Message);

/// <summary>The report a roster import returns (FR-50): who was seated, which rows were not and why, and what was ignored.</summary>
/// <param name="Added">The rows that were given a seat, in file order.</param>
/// <param name="Skipped">The rows that were not, in file order. A skipped row never stops the rows after it.</param>
/// <param name="IgnoredColumns">The columns of the file that a roster does not use.</param>
public sealed record ImportBatchRosterResultDto(
    IReadOnlyList<RosterAddedRowDto> Added,
    IReadOnlyList<RosterSkippedRowDto> Skipped,
    IReadOnlyList<string> IgnoredColumns)
{
    /// <summary>Builds the report from what the batch decided for each row.</summary>
    /// <param name="results">One result per row, in file order.</param>
    /// <param name="ignoredColumns">The columns the reader left out.</param>
    /// <exception cref="InvalidOperationException">A result has an outcome this report has no wording for.</exception>
    public static ImportBatchRosterResultDto From(IReadOnlyList<RosterRowResult> results, IReadOnlyList<string> ignoredColumns)
    {
        var added = new List<RosterAddedRowDto>();
        var skipped = new List<RosterSkippedRowDto>();

        foreach (var result in results)
        {
            if (result.Outcome == RosterRowOutcome.Added)
                added.Add(new RosterAddedRowDto(result.Row, result.MemberId!.Value));
            else
                skipped.Add(Describe(result));
        }

        return new ImportBatchRosterResultDto(added, skipped, ignoredColumns);
    }

    private static RosterSkippedRowDto Describe(RosterRowResult result) => result.Outcome switch
    {
        RosterRowOutcome.MissingEmail => new(result.Row, "missing_email", "The row has no e-mail address."),
        RosterRowOutcome.InvalidEmail => new(result.Row, "invalid_email", "The e-mail address is not valid."),
        RosterRowOutcome.InvalidPhone => new(result.Row, "invalid_phone", "The phone number is not valid. Leave it blank to add the candidate without one."),
        RosterRowOutcome.AlreadyInBatch => new(result.Row, "already_in_batch", "The address already has a seat in this batch."),
        RosterRowOutcome.RepeatedInFile => new(result.Row, "repeated_in_file", $"The same address as row {result.FirstRow}, which was given the seat."),
        RosterRowOutcome.BatchFull => new(result.Row, "batch_full", "The batch has no seat left."),
        _ => throw new InvalidOperationException($"No report wording for roster outcome '{result.Outcome}'."),
    };
}
