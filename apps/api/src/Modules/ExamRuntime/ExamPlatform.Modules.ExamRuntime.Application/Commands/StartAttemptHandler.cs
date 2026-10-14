using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Guardian.Contracts;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Starts a candidate's next attempt at an exam, or returns the one they are in the middle of (FR-16, FR-17).</summary>
public sealed class StartAttemptHandler(
    IExamCatalog catalog,
    IEnrollments enrollments,
    IAttemptRepository attempts,
    IExtraAttemptGrantRepository grants,
    IAccommodationRepository accommodations,
    IExamRuntimeUnitOfWork unitOfWork,
    AttemptAccess access,
    AttemptViewBuilder views,
    PaperDrawer paperDrawer,
    IQuestionBank questionBank,
    Clock clock,
    IClientInfo clientInfo,
    ICandidateAgeDirectory ages,
    IGuardianLinkDirectory guardians,
    IProctorPolicy proctorPolicy)
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
    /// <exception cref="GuardianConsentRequiredError">The candidate is under 18 and no guardian has confirmed the link to them.</exception>
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
            var theirExam = exam.For(latest, proctorPolicy);
            await access.CloseIfExpiredAsync(latest, theirExam, cancellationToken);

            // Resuming from another address or device is exactly what multi-login detection looks for (FR-26): note it, and keep it.
            if (latest.NoteClient(clientInfo.IpAddress, clientInfo.DeviceFingerprint, clock.UtcNow))
                await unitOfWork.SaveChangesAsync(cancellationToken);

            return await views.BuildAsync(latest, theirExam, cancellationToken);
        }

        var granted = await grants.CountAsync(examId, candidateId, cancellationToken);
        if (!AttemptAllowance.CanStartAnother(exam.MaxAttempts, theirs.Count, granted))
            return await views.BuildAsync(latest!, exam.For(latest!, proctorPolicy), cancellationToken);

        // Checked after the allowance, so "nothing left" is still answered first, and before the instructions and the time checks, so a
        // minor without a guardian is told why they cannot start.
        await EnsureMinorHasGuardianAsync(candidateId, cancellationToken);

        // Checked here, after the cases that need no new attempt, so resuming and "nothing left" stay safe to repeat. The server
        // enforces it, not just the page, because the acknowledgment is the point of FR-17 and a client can be bypassed.
        if (!instructionsAcknowledged)
            throw new InstructionsNotAcknowledgedError();

        var attempt = Begin(exam, candidateId, theirs.Count + 1);
        // Extra time and formats are part of the attempt from its first moment, so the deadline the server holds already includes them (FR-49).
        if (await accommodations.FindAsync(examId, candidateId, cancellationToken) is { } accommodation)
            attempt.ApplyAccommodation(accommodation.ExtraTimeSeconds, accommodation.ReaderScribe, accommodation.AlternateFormats);
        // The acknowledgment is the start: one instant, so the record cannot disagree with the attempt's own clock.
        // What the candidate was told about proctoring is kept as shown, so the record says what they acknowledged (FR-46).
        attempt.AcknowledgeInstructions(attempt.StartedAtUtc, exam.ProctoringNotice is { Count: > 0 } notice ? string.Join("\n", notice) : null);
        // Where the candidate began, the first of the attempt's sightings (FR-26).
        attempt.NoteClient(clientInfo.IpAddress, clientInfo.DeviceFingerprint, attempt.StartedAtUtc);
        if (PaperDrawer.Draws(exam))
            attempt.SetPaper(await paperDrawer.DrawAsync(exam, cancellationToken));

        // Which version of each question this attempt sits is fixed now, so editing a question later cannot change this attempt (FR-7).
        var paperIds = exam.For(attempt, proctorPolicy).Sections.SelectMany(s => s.QuestionIds).Distinct().ToList();
        attempt.PinQuestionVersions((await questionBank.GetAsync(paperIds, cancellationToken)).ToDictionary(q => q.Id, q => q.VersionNumber));

        attempts.Add(attempt);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await views.BuildAsync(attempt, exam.For(attempt, proctorPolicy), cancellationToken);
    }

    /// <summary>
    /// Refuses a new attempt by a candidate who is under 18 today unless a guardian has confirmed the link to them. The age rule, and its
    /// fail-safe for a candidate with no date of birth on record, belong to Identity.
    /// </summary>
    /// <exception cref="GuardianConsentRequiredError">The candidate is a minor and no guardian has confirmed the link.</exception>
    private async Task EnsureMinorHasGuardianAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        if (!await ages.IsMinorOnAsync(candidateId, clock.UtcNow, cancellationToken))
            return;

        if (!await guardians.HasConfirmedGuardianAsync(candidateId, cancellationToken))
            throw new GuardianConsentRequiredError();
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
