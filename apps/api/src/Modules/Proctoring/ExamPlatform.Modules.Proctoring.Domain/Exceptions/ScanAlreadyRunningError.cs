using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Proctoring.Domain.Exceptions;

/// <summary>
/// Another scan of the same exam wrote an assessment for an attempt while this one was writing it, so this scan's changes were refused
/// (409). Nothing from this scan was saved, and the reviewer can run the scan again once the other has finished.
/// </summary>
/// <param name="innerException">The database conflict that revealed the overlap.</param>
public sealed class ScanAlreadyRunningError(Exception innerException)
    : DomainException("Another scan of this exam is already running. Wait for it to finish, then look at the queue again.", innerException)
{
    /// <inheritdoc />
    public override string ErrorCode => "scan_already_running";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
