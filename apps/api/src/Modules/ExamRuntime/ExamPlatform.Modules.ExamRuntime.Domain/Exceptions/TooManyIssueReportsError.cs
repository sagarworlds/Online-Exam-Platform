using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The candidate has already reported as many problems in this attempt as one attempt may carry.</summary>
public sealed class TooManyIssueReportsError()
    : DomainException($"You have already reported {IssueReport.MaxPerAttempt} problems in this attempt. Staff have them; please wait for a reply.")
{
    /// <inheritdoc />
    public override string ErrorCode => "too_many_issue_reports";

    /// <inheritdoc />
    public override int HttpStatusCode => 429;
}
