using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>
/// Lets a candidate dispute the answer key of one question in one of their released results (FR-31). This is the "review and dispute
/// window": a result can be disputed only once the candidate can see the answers, and only for a limited time afterwards.
/// </summary>
public sealed class RaiseDisputeHandler(
    AttemptAccess access, IDisputeRepository disputes, IExamRuntimeUnitOfWork unitOfWork, DisputePolicy policy, Clock clock)
{
    /// <summary>Records the dispute, to be settled by staff.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate, taken from their token.</param>
    /// <param name="questionId">The question whose answer key they dispute.</param>
    /// <param name="reason">Why they think the key is wrong; required, at most 1000 characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dispute as recorded.</returns>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open.</exception>
    /// <exception cref="AttemptInvalidatedError">An administrator invalidated the result, so there is nothing to dispute.</exception>
    /// <exception cref="ResultsNotReleasedError">The candidate cannot see the answers yet, so has nothing to dispute.</exception>
    /// <exception cref="DisputeWindowClosedError">Disputes are switched off, or the time allowed since the result was released has passed.</exception>
    /// <exception cref="QuestionNotInAttemptError">The question was not part of this attempt.</exception>
    /// <exception cref="DisputeAlreadyRaisedError">They already disputed this question in this attempt.</exception>
    /// <exception cref="InvalidAttemptError">The reason is missing or too long.</exception>
    /// <exception cref="ConcurrencyConflictError">The same dispute was recorded at the same moment.</exception>
    public async Task<MyDisputeDto> HandleAsync(
        Guid attemptId, Guid candidateId, Guid questionId, string? reason, CancellationToken cancellationToken)
    {
        // Ownership first: someone else's attempt answers exactly like a missing one, before anything about it is revealed.
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (attempt.Status != AttemptStatus.Submitted)
            throw new AttemptNotSubmittedError();
        if (attempt.IsInvalidated)
            throw new AttemptInvalidatedError();

        var nowUtc = clock.UtcNow;

        // A key can only be disputed by someone who has seen it.
        var availability = ResultRelease.AvailabilityOf(exam, nowUtc);
        if (!availability.Available)
            throw new ResultsNotReleasedError(availability.AvailableFromUtc);

        var window = policy.WindowFor(exam, attempt, nowUtc);
        if (!window.Enabled)
            throw new DisputeWindowClosedError("Disputes are not being taken.");
        if (!window.Open)
            throw new DisputeWindowClosedError($"The time to dispute this result ended on {window.ClosesAtUtc:yyyy-MM-dd HH:mm} UTC.");

        // The exam is already the one this attempt sat, drawn paper included.
        if (!exam.Sections.SelectMany(s => s.QuestionIds).Contains(questionId))
            throw new QuestionNotInAttemptError();

        if (await disputes.ExistsAsync(attemptId, questionId, cancellationToken))
            throw new DisputeAlreadyRaisedError();

        var dispute = Dispute.Raise(attemptId, attempt.ExamId, candidateId, questionId, reason, nowUtc);
        disputes.Add(dispute);
        // The store allows one dispute per question per attempt, so two taps at once cannot queue two.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DisputeDtoFactory.ForCandidate(dispute);
    }
}

/// <summary>Lists the disputes candidates have raised, for staff to work through.</summary>
public sealed class ListDisputesHandler(IDisputeRepository disputes, DisputeDtoFactory dtos)
{
    /// <summary>How many disputes one listing returns at most.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the disputes with the given status, oldest first.</summary>
    /// <param name="status">Which disputes to list; open ones when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<DisputeDto>> HandleAsync(DisputeStatus? status, CancellationToken cancellationToken) =>
        await dtos.CreateAsync(await disputes.ListAsync(status ?? DisputeStatus.Open, PageSize, cancellationToken), cancellationToken);
}

/// <summary>
/// Lets staff leave an answer key as it is and tell the candidate why. Accepting a dispute is not a command of its own: it is what
/// correcting the question's answer key does to every open dispute about that question (see <see cref="AttemptRescorer"/>), so an
/// acceptance can never exist without the correction that justifies it.
/// </summary>
public sealed class RejectDisputeHandler(IDisputeRepository disputes, IExamRuntimeUnitOfWork unitOfWork, DisputeDtoFactory dtos, Clock clock)
{
    /// <summary>Marks the dispute rejected and saves.</summary>
    /// <param name="disputeId">The dispute.</param>
    /// <param name="decidedByUserId">The signed-in staff user, taken from their token.</param>
    /// <param name="note">Why the key stands; required, at most 500 characters. The candidate sees it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="DisputeNotFoundError">No dispute has that id.</exception>
    /// <exception cref="DisputeNotOpenError">The dispute was already settled.</exception>
    /// <exception cref="InvalidAttemptError">The note is missing or too long.</exception>
    public async Task<DisputeDto> HandleAsync(Guid disputeId, Guid decidedByUserId, string? note, CancellationToken cancellationToken)
    {
        var dispute = await disputes.GetByIdAsync(disputeId, cancellationToken) ?? throw new DisputeNotFoundError();

        dispute.Reject(decidedByUserId, clock.UtcNow, note);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await dtos.CreateAsync([dispute], cancellationToken))[0];
    }
}
