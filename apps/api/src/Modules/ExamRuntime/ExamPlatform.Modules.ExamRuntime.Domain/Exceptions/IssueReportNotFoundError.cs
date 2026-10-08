using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>No reported problem has that id.</summary>
public sealed class IssueReportNotFoundError() : DomainException("No reported problem has that id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "issue_report_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
