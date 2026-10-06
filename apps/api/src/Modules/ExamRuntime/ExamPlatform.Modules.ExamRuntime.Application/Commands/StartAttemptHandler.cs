using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Starts a candidate's next attempt at an exam, or returns the one they are in the middle of (FR-16, FR-17).</summary>
public sealed class StartAttemptHandler(
    IExamCatalog catalog,
    IEnrollments enrollments,
    IAttemptRepository attempts,
    IExtraAttemptGrantRepository grants,
    IExamRuntimeUnitOfWork unitOfWork,
    AttemptAccess access,
    AttemptViewBuilder views,
    PaperDrawer paperDrawer,
    Clock clock,
    IClientInfo clientInfo)
{
    /// <summary>
    /// Starts the attempt. Calling it again while an attempt is open is how a candidate resumes: it returns that attempt with its
    /// original deadline. Once every attempt they are allowed has been used (the exam's limit, plus any an administrator granted) it returns
    /// their latest attempt, so the call stays safe to repeat and never creates one nobody allowed.
    /// </summary>
    /// <param name="examId">The exam to take.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="instructionsAcknowledged">
    /// Whether the candidate confirmed they read the instructions. Only a new attempt needs it: resuming one already begun, or being told
    /// there is none left, does not.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InstructionsNotAcknowledgedError">A new attempt would begin but the instructions were not acknowledged.</exception>
    /// <exception cref="ExamNotAvailableError">The exam does not exist, is not published, or the candidate is not enrolled in it.</exception>
    /// <exception cref="ExamNotOpenError">The window has not opened yet.</exception>
    /// <exception cref="ExamClosedError">The window, or the late-entry cutoff, has passed.</exception>
    /// <exception cref="PaperCannotBeDrawnError">A draw rule of the exam finds fewer questions than it needs.</exception>
    /// <exception cref="ConcurrencyConflictError">Another request started the same attempt at the same moment.</exception>
    public async Task<AttemptDto> HandleAsync(Guid examId, Guid candidateId, bool instructionsAcknowledged, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken);

        // One answer for "no such exam", "not published" and "not invited", so an exam id cannot be probed.
        if (exam is null || !exam.IsPublished || !await enrollments.IsEnrolledAsync(candidateId, examId, cancellationToken))
            throw new ExamNotAvailableError();

        var theirs = await attempts.ListForCandidateAtExamAsync(examId, candidateId, cancellationToken);
        var latest = theirs.Count > 0 ? theirs[^1] : null;

        // An attempt in the middle is resumed, never followed by a new one in the same call: if its time has run out it is
        // closed here and its result returned, and starting the next is a separate, deliberate request.
        if (latest is { Status: AttemptStatus.InProgress })
        {
            var theirExam = exam.For(latest);
            await access.CloseIfExpiredAsync(latest, theirExam, cancellationToken);

            // Resuming from another address or device is exactly what multi-login detection looks for (FR-26): note it, and keep it.
            if (latest.NoteClient(clientInfo.IpAddress, clientInfo.DeviceFingerprint, clock.UtcNow))
                await unitOfWork.SaveChangesAsync(cancellationToken);

            return await views.BuildAsync(latest, theirExam, cancellationToken);
        }

        var granted = await grants.CountAsync(examId, candidateId, cancellationToken);
        if (!AttemptAllowance.CanStartAnother(exam.MaxAttempts, theirs.Count, granted))
            return await views.BuildAsync(latest!, exam.For(latest!), cancellationToken);

        // Checked here, after the cases that need no new attempt, so resuming and "nothing left" stay safe to repeat. The server
        // enforces it, not just the page, because the acknowledgment is the point of FR-17 and a client can be bypassed.
        if (!instructionsAcknowledged)
            throw new InstructionsNotAcknowledgedError();

        var attempt = Begin(exam, candidateId, theirs.Count + 1);
        // The acknowledgment is the start: one instant, so the record cannot disagree with the attempt's own clock.
        // What the candidate was told about proctoring is kept as shown, so the record says what they acknowledged (FR-46).
        attempt.AcknowledgeInstructions(attempt.StartedAtUtc, exam.ProctoringNotice is { Count: > 0 } notice ? string.Join("\n", notice) : null);
        // Where the candidate began, the first of the attempt's sightings (FR-26).
        attempt.NoteClient(clientInfo.IpAddress, clientInfo.DeviceFingerprint, attempt.StartedAtUtc);
        if (PaperDrawer.Draws(exam))
            attempt.SetPaper(await paperDrawer.DrawAsync(exam, cancellationToken));

        attempts.Add(attempt);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await views.BuildAsync(attempt, exam.For(attempt), cancellationToken);
    }

    private Attempt Begin(ExamSnapshot exam, Guid candidateId, int number)
    {
        var nowUtc = clock.UtcNow;
        if (nowUtc < exam.StartUtc)
            throw new ExamNotOpenError();

        var deadlineUtc = ExamWindow.DeadlineUtc(exam, nowUtc);
        // The late-entry cutoff, and a deadline that has already arrived, both mean there is no time left to sit it.
        if (!ExamWindow.CanStart(exam, nowUtc) || deadlineUtc <= nowUtc)
            throw new ExamClosedError();

        return Attempt.Start(exam.Id, candidateId, number, nowUtc, deadlineUtc);
    }
}
