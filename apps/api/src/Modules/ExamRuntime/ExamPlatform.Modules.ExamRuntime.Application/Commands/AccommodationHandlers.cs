using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>What staff ask for when they set a candidate's accommodation.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The enrolled candidate.</param>
/// <param name="ByUserId">The signed-in staff user, taken from their token and never from the request.</param>
/// <param name="ExtraTimeSeconds">Extra time in seconds; 0 for none.</param>
/// <param name="ReaderScribe">Whether a reader or scribe is allowed.</param>
/// <param name="AlternateFormats">The formats, by code. Null means none.</param>
/// <param name="Notes">A note for staff.</param>
public sealed record SetAccommodationCommand(
    Guid ExamId, Guid CandidateId, Guid ByUserId, int ExtraTimeSeconds, bool ReaderScribe, IReadOnlyList<string?>? AlternateFormats, string? Notes);

/// <summary>Gives a candidate an accommodation at an exam, or changes the one they have (FR-49).</summary>
public sealed class SetAccommodationHandler(
    IExamCatalog catalog,
    IExamRoster roster,
    IAttemptRepository attempts,
    IExtraAttemptGrantRepository grants,
    IAccommodationRepository accommodations,
    IExamRuntimeUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>
    /// Stores the accommodation, and applies it to the attempt the candidate is in the middle of, if any: more time moves that attempt's
    /// deadline later at once, and the heartbeat the exam page already makes shows the candidate the new end time. An attempt whose time
    /// has already run out is left alone, so setting an accommodation can never reopen a sitting that is over.
    /// </summary>
    /// <param name="command">What to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How the candidate now stands, as staff see them.</returns>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="CandidateNotEnrolledError">The person never accepted an invitation to the exam.</exception>
    /// <exception cref="InvalidAccommodationError">The accommodation gives nothing, or a value is out of range.</exception>
    /// <exception cref="ConcurrencyConflictError">Another administrator set the same candidate's accommodation at the same moment.</exception>
    public async Task<ExamCandidateDto> HandleAsync(SetAccommodationCommand command, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(command.ExamId, cancellationToken) ?? throw new ExamNotFoundError();
        var candidate = (await roster.GetEnrolledCandidatesAsync(command.ExamId, cancellationToken)).FirstOrDefault(c => c.UserId == command.CandidateId)
            ?? throw new CandidateNotEnrolledError();

        var nowUtc = clock.UtcNow;
        var accommodation = await accommodations.FindAsync(command.ExamId, command.CandidateId, cancellationToken);
        if (accommodation is null)
        {
            accommodation = Accommodation.Create(
                command.ExamId, command.CandidateId, command.ExtraTimeSeconds, command.ReaderScribe, command.AlternateFormats, command.Notes, command.ByUserId, nowUtc);
            accommodations.Add(accommodation);
        }
        else
        {
            accommodation.Revise(command.ExtraTimeSeconds, command.ReaderScribe, command.AlternateFormats, command.Notes, command.ByUserId, nowUtc);
        }

        var theirs = await attempts.ListForCandidateAtExamAsync(command.ExamId, command.CandidateId, cancellationToken);
        if (theirs.Count > 0 && theirs[^1] is { Status: AttemptStatus.InProgress } open && !open.IsExpired(nowUtc))
            open.ApplyAccommodation(accommodation.ExtraTimeSeconds, accommodation.ReaderScribe, accommodation.AlternateFormats);

        // One row per candidate per exam, unique in the store, so two administrators setting it at once cannot both create it.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ExamCandidateRows.For(exam, candidate, theirs, await grants.CountAsync(command.ExamId, command.CandidateId, cancellationToken), nowUtc, accommodation);
    }
}

/// <summary>Takes a candidate's accommodation away (FR-49).</summary>
public sealed class RemoveAccommodationHandler(
    IExamCatalog catalog,
    IExamRoster roster,
    IAttemptRepository attempts,
    IExtraAttemptGrantRepository grants,
    IAccommodationRepository accommodations,
    IExamRuntimeUnitOfWork unitOfWork,
    IAuditLogger auditLogger,
    IRequestContext requestContext,
    Clock clock)
{
    /// <summary>
    /// Removes the accommodation for the attempts that start afterwards. An attempt already in progress keeps what it was given: the
    /// candidate was told their time, and taking it back mid-exam would be worse than the oversight it corrects.
    /// </summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The enrolled candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How the candidate now stands, as staff see them.</returns>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="CandidateNotEnrolledError">The person never accepted an invitation to the exam.</exception>
    /// <exception cref="AccommodationNotFoundError">The candidate has no accommodation at the exam.</exception>
    public async Task<ExamCandidateDto> HandleAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamNotFoundError();
        var candidate = (await roster.GetEnrolledCandidatesAsync(examId, cancellationToken)).FirstOrDefault(c => c.UserId == candidateId)
            ?? throw new CandidateNotEnrolledError();
        var accommodation = await accommodations.FindAsync(examId, candidateId, cancellationToken) ?? throw new AccommodationNotFoundError();

        accommodations.Remove(accommodation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Recorded here rather than from an event: a deleted row is no longer tracked when events are dispatched, so it would never be.
        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: "ExamRuntime.AccommodationRemoved",
                EntityType: "Accommodation",
                EntityId: accommodation.Id.ToString(),
                Metadata: new Dictionary<string, string> { ["examId"] = examId.ToString(), ["candidateId"] = candidateId.ToString() },
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);

        var theirs = await attempts.ListForCandidateAtExamAsync(examId, candidateId, cancellationToken);
        return ExamCandidateRows.For(exam, candidate, theirs, await grants.CountAsync(examId, candidateId, cancellationToken), clock.UtcNow, null);
    }
}
