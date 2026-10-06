namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>One line of a question's review thread (FR-8).</summary>
/// <param name="Id">The entry's id.</param>
/// <param name="Kind">"commented", "submitted", "approved", "changes_requested", "retired" or "restored".</param>
/// <param name="ByLabel">Who wrote it, as it should be shown.</param>
/// <param name="Comment">The comment; empty when none was given.</param>
/// <param name="VersionNumber">The version of the question that was current at the time.</param>
/// <param name="StatusAfter">The question's status afterwards.</param>
/// <param name="CreatedAtUtc">When it happened.</param>
public sealed record QuestionReviewEntryDto(
    Guid Id, string Kind, string ByLabel, string Comment, int VersionNumber, string StatusAfter, DateTime CreatedAtUtc);

/// <summary>What a review step did: the question's status afterwards, and the thread entry that records it.</summary>
/// <param name="Status">The question's status afterwards.</param>
/// <param name="Entry">The entry added to its thread.</param>
public sealed record QuestionReviewResultDto(string Status, QuestionReviewEntryDto Entry);
