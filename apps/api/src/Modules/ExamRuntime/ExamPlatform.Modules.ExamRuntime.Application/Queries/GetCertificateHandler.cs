using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>
/// Works out what a candidate's certificate says (FR-34). A certificate is issued only for a result the exam's author has released, the same
/// gate the result page uses, so a certificate never shows a score that candidates may not yet see. The requirement's "passed" condition has no
/// pass mark to test in the exam model, so it is not applied here.
/// </summary>
public sealed class GetCertificateHandler(AttemptAccess access, IDisplayNameDirectory displayNames, Clock clock)
{
    /// <summary>Returns the details of the certificate for one of the candidate's own submitted attempts.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the certificate says.</returns>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open.</exception>
    /// <exception cref="AttemptInvalidatedError">An administrator invalidated the result, so it has no certificate.</exception>
    /// <exception cref="ResultsNotReleasedError">The exam's author has not released the results yet.</exception>
    /// <exception cref="CertificateNameMissingError">The candidate's account has no name to print.</exception>
    /// <exception cref="CertificateTextUnsupportedError">The name or the exam's name uses a character the certificate cannot show.</exception>
    public async Task<CertificateDetails> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (attempt.IsInvalidated)
            throw new AttemptInvalidatedError();
        if (attempt.Status != AttemptStatus.Submitted || attempt.Score is null || attempt.MaxScore is null)
            throw new AttemptNotSubmittedError();

        var availability = ResultRelease.AvailabilityOf(exam, clock.UtcNow);
        if (!availability.Available)
            throw new ResultsNotReleasedError(availability.AvailableFromUtc);

        var names = await displayNames.GetDisplayNamesAsync([candidateId], cancellationToken);
        if (!names.TryGetValue(candidateId, out var name) || string.IsNullOrWhiteSpace(name))
            throw new CertificateNameMissingError();

        var printedName = name.Trim();
        if (!CertificatePdf.CanShow(printedName) || !CertificatePdf.CanShow(exam.Name))
            throw new CertificateTextUnsupportedError();

        return new CertificateDetails(
            printedName,
            exam.Name,
            attempt.SubmittedAtUtc ?? clock.UtcNow,
            attempt.Score.Value,
            attempt.MaxScore.Value,
            attempt.Id);
    }
}
