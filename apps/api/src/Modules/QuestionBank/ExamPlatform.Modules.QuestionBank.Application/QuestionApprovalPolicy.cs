using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>
/// Which questions may go into an exam (FR-8): a retired question never may; and when the deployment sets
/// <c>QuestionBank:RequireApproval</c>, only an approved one may. Off by default, so a bank that has no reviewers is not locked out of
/// building exams. The bank owns this rule and tells other modules the answer (<c>QuestionSnapshot.UnusableReason</c>), so they never
/// read a status.
/// </summary>
/// <param name="RequireApproval">Whether a question must be approved before an exam may hold it.</param>
public sealed record QuestionApprovalPolicy(bool RequireApproval)
{
    /// <summary>The statuses an exam may take questions from.</summary>
    public IReadOnlyList<QuestionStatus> UsableStatuses { get; } =
        RequireApproval ? [QuestionStatus.Approved] : [QuestionStatus.Draft, QuestionStatus.InReview, QuestionStatus.Approved];

    /// <summary>Why a question in this status cannot go into an exam, or null when it can.</summary>
    /// <param name="status">The question's status.</param>
    public string? UnusableReason(QuestionStatus status) =>
        UsableStatuses.Contains(status)
            ? null
            : status == QuestionStatus.Retired
                ? "The question is retired."
                : "This deployment only allows approved questions in exams, and the question is not approved yet.";
}
