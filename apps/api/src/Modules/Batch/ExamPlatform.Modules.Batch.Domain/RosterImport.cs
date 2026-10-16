namespace ExamPlatform.Modules.Batch.Domain;

/// <summary>One row of a roster file, as read, before it is checked (FR-50).</summary>
/// <param name="Row">Where the row is in the file, counting a header as row 1, so a report can point at it.</param>
/// <param name="Email">The e-mail address as written; null when the row has none.</param>
/// <param name="Phone">The phone number as written; null when the row has none.</param>
public sealed record RosterRow(int Row, string? Email, string? Phone);

/// <summary>What happened to one row of a roster file when it was imported into a batch.</summary>
public enum RosterRowOutcome
{
    /// <summary>The row was given a seat.</summary>
    Added,

    /// <summary>The row has no e-mail address.</summary>
    MissingEmail,

    /// <summary>The e-mail address is not one plain, usable address.</summary>
    InvalidEmail,

    /// <summary>The phone number is not usable. The e-mail address alone would have been.</summary>
    InvalidPhone,

    /// <summary>The address already holds a seat in the batch, so nothing was added.</summary>
    AlreadyInBatch,

    /// <summary>An earlier row of the same file already gave the address a seat, so nothing more was added.</summary>
    RepeatedInFile,

    /// <summary>The batch had no seat left for the row.</summary>
    BatchFull,
}

/// <summary>The outcome of one row, with what a report needs to say about it.</summary>
/// <param name="Row">The row's number in the file, as in <see cref="RosterRow.Row"/>.</param>
/// <param name="Outcome">What happened to the row.</param>
/// <param name="MemberId">The new member's id when the row was added; otherwise null.</param>
/// <param name="FirstRow">For a <see cref="RosterRowOutcome.RepeatedInFile"/> row, the row that took the seat; otherwise null.</param>
public sealed record RosterRowResult(int Row, RosterRowOutcome Outcome, Guid? MemberId = null, int? FirstRow = null);
