using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Application.Exceptions;

/// <summary>No notice version matches the id supplied when recording consent.</summary>
public sealed class NoticeVersionNotFoundError() : DomainException("No matching notice version was found.")
{
    /// <inheritdoc />
    public override string ErrorCode => "notice_version_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
