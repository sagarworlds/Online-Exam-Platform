using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The reported problem was already marked resolved, which is a record of who dealt with it and when, not something to redo.</summary>
public sealed class IssueReportNotOpenError() : DomainException("This reported problem has already been resolved.")
{
    /// <inheritdoc />
    public override string ErrorCode => "issue_report_not_open";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
