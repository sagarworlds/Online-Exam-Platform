using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Starts a candidate's attempt at an exam, or returns the one they already have (FR-16, FR-17).</summary>
public sealed class StartAttemptHandler(
    IExamCatalog catalog,
    IEnrollments enrollments,
    IAttemptRepository attempts,
    IExamRuntimeUnitOfWork unitOfWork,
    AttemptAccess access,
    AttemptViewBuilder views,
    Clock clock)
{
    /// <summary>
    /// Starts the attempt. Calling it again for the same exam is how a candidate resumes: it returns the same
    /// attempt rather than creating another, with the original deadline.
    /// </summary>
    /// <param name="examId">The exam to take.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotAvailableError">The exam does not exist, is not published, or the candidate is not enrolled in it.</exception>
    /// <exception cref="ExamNotOpenError">The window has not opened yet.</exception>
    /// <exception cref="ExamClosedError">The window, or the late-entry cutoff, has passed.</exception>
    /// <exception cref="ConcurrencyConflictError">Another request started the same attempt at the same moment.</exception>
    public async Task<AttemptDto> HandleAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken);

        // One answer for "no such exam", "not published" and "not invited", so an exam id cannot be probed.
        if (exam is null || !exam.IsPublished || !await enrollments.IsEnrolledAsync(candidateId, examId, cancellationToken))
            throw new ExamNotAvailableError();

        var attempt = await attempts.FindAsync(examId, candidateId, cancellationToken);
        if (attempt is null)
        {
            attempt = Begin(exam, candidateId);
            attempts.Add(attempt);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await access.CloseIfExpiredAsync(attempt, exam, cancellationToken);
        }

        return await views.BuildAsync(attempt, exam, cancellationToken);
    }

    private Attempt Begin(ExamSnapshot exam, Guid candidateId)
    {
        var nowUtc = clock.UtcNow;
        if (nowUtc < exam.StartUtc)
            throw new ExamNotOpenError();

        var deadlineUtc = ExamWindow.DeadlineUtc(exam, nowUtc);
        // The late-entry cutoff, and a deadline that has already arrived, both mean there is no time left to sit it.
        if (!ExamWindow.CanStart(exam, nowUtc) || deadlineUtc <= nowUtc)
            throw new ExamClosedError();

        return Attempt.Start(exam.Id, candidateId, nowUtc, deadlineUtc);
    }
}
