using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>
/// Works out what a candidate's certificate says (FR-34). A certificate is issued for a result the exam's author has released, the same gate
/// the result page uses, or for an attempt that passed the exam's pass mark (the requirement's "passed or released"). A certificate shows the
/// score only once the result is released, so a pass before release says that the candidate passed and gives no number. With no pass mark
/// on the exam, a pass cannot be judged, and only a released result gets a certificate.
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
    /// <exception cref="ResultsNotReleasedError">The exam's author has not released the results yet, and the candidate did not pass.</exception>
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
        var passed = PassRule.Of(attempt.Score.Value, attempt.MaxScore.Value, exam.PassingPercentage) == true;
        if (!availability.Available && !passed)
            throw new ResultsNotReleasedError(availability.AvailableFromUtc);

        var names = await displayNames.GetDisplayNamesAsync([candidateId], cancellationToken);
        if (!names.TryGetValue(candidateId, out var name) || string.IsNullOrWhiteSpace(name))
            throw new CertificateNameMissingError();

        var printedName = name.Trim();
        if (!CertificatePdf.CanShow(printedName) || !CertificatePdf.CanShow(exam.Name))
            throw new CertificateTextUnsupportedError();

        // The score is printed only once the result is released; a pass before that says so without the number.
        var printedScore = availability.Available ? (decimal?)attempt.Score.Value : null;
        var printedMaxScore = availability.Available ? (decimal?)attempt.MaxScore.Value : null;

        return new CertificateDetails(
            printedName,
            exam.Name,
            attempt.SubmittedAtUtc ?? clock.UtcNow,
            printedScore,
            printedMaxScore,
            attempt.Id);
    }
}
