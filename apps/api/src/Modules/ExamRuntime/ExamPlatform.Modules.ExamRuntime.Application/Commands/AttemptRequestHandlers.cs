using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Lets a candidate ask for one more attempt at an exam once they have used the ones they hold.</summary>
public sealed class RequestAttemptHandler(
    IExamCatalog catalog,
    IEnrollments enrollments,
    IAttemptRepository attempts,
    IExtraAttemptGrantRepository grants,
    IAttemptRequestRepository requests,
    IExamRuntimeUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Records the request, to be decided by an administrator.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The signed-in candidate, taken from their token.</param>
    /// <param name="message">Why they want another attempt; optional, at most 500 characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request as recorded.</returns>
    /// <exception cref="ExamNotAvailableError">The exam does not exist, is not published, or the candidate is not enrolled in it.</exception>
    /// <exception cref="ExamClosedError">Nobody can start the exam any more, so another attempt could never be used.</exception>
    /// <exception cref="AttemptNotNeededError">They still have an attempt left, or one in progress.</exception>
    /// <exception cref="AttemptOverLimitError">The exam's limit was lowered below the attempts they have made, so another would change nothing.</exception>
    /// <exception cref="AttemptRequestPendingError">An earlier request of theirs for this exam is still waiting.</exception>
    /// <exception cref="InvalidAttemptError">The message is too long.</exception>
    /// <exception cref="ConcurrencyConflictError">Another request of theirs for this exam was recorded at the same moment.</exception>
    public async Task<MyAttemptRequestDto> HandleAsync(Guid examId, Guid candidateId, string? message, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken);

        // One answer for "no such exam", "not published" and "not invited", as everywhere a candidate acts, so ids cannot be probed.
        if (exam is null || !exam.IsPublished || !await enrollments.IsEnrolledAsync(candidateId, examId, cancellationToken))
            throw new ExamNotAvailableError();

        var nowUtc = clock.UtcNow;
        if (ExamCandidateRows.IsWindowClosed(exam, nowUtc))
            throw new ExamClosedError();

        var theirs = await attempts.ListForCandidateAtExamAsync(examId, candidateId, cancellationToken);
        var granted = await grants.CountAsync(examId, candidateId, cancellationToken);
        if (AttemptAllowance.IsOverLimit(exam.MaxAttempts, theirs.Count, granted))
            throw new AttemptOverLimitError();

        // Only once every attempt they hold is used: the same moment an administrator may grant one, so an approval can always follow.
        if (!AttemptAllowance.CanGrant(exam.MaxAttempts, theirs.Count, granted) || theirs.Any(a => a.Status == AttemptStatus.InProgress))
            throw new AttemptNotNeededError();

        if (await requests.HasPendingAsync(examId, candidateId, cancellationToken))
            throw new AttemptRequestPendingError();

        var request = AttemptRequest.Create(examId, candidateId, message, nowUtc);
        requests.Add(request);
        // The store allows one pending request per candidate per exam, so two taps at once cannot queue two.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AttemptRequestDtoFactory.ForCandidate(request);
    }
}

/// <summary>Lists the requests for another attempt, for staff to work through.</summary>
public sealed class ListAttemptRequestsHandler(IAttemptRequestRepository requests, AttemptRequestDtoFactory dtos)
{
    /// <summary>How many requests one listing returns at most.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the requests with the given status, oldest first.</summary>
    /// <param name="status">Which requests to list; waiting ones when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<AttemptRequestDto>> HandleAsync(AttemptRequestStatus? status, CancellationToken cancellationToken) =>
        await dtos.CreateAsync(await requests.ListAsync(status ?? AttemptRequestStatus.Pending, PageSize, cancellationToken), cancellationToken);
}

/// <summary>Lets an administrator approve a request, which gives the candidate the attempt.</summary>
public sealed class ApproveAttemptRequestHandler(
    IAttemptRequestRepository requests, GrantExtraAttemptHandler grantHandler, AttemptRequestDtoFactory dtos)
{
    /// <summary>Grants the attempt and marks the request approved, in one save.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="decidedByUserId">The signed-in staff user, taken from their token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptRequestNotFoundError">No request has that id.</exception>
    /// <exception cref="AttemptRequestNotPendingError">The request was already decided.</exception>
    /// <exception cref="ExamClosedError">Nobody can start the exam any more, so an attempt granted now could never be used.</exception>
    /// <exception cref="AttemptAvailableError">The candidate has an attempt they have not used (an administrator may have granted one directly).</exception>
    /// <exception cref="AttemptOverLimitError">The exam's limit was lowered below the attempts they have made.</exception>
    /// <exception cref="ConcurrencyConflictError">Another administrator granted an attempt to the same candidate at the same moment.</exception>
    public async Task<AttemptRequestDto> HandleAsync(Guid requestId, Guid decidedByUserId, CancellationToken cancellationToken)
    {
        var request = await requests.GetByIdAsync(requestId, cancellationToken) ?? throw new AttemptRequestNotFoundError();

        // Checked before the grant so an already-decided request is refused with its own error, not a misleading "attempt left".
        if (request.Status != AttemptRequestStatus.Pending)
            throw new AttemptRequestNotPendingError();

        await grantHandler.HandleAsync(request.ExamId, request.CandidateId, decidedByUserId, request.Message, cancellationToken, fulfilling: request);

        return (await dtos.CreateAsync([request], cancellationToken))[0];
    }
}

/// <summary>Lets an administrator turn a request down.</summary>
public sealed class DeclineAttemptRequestHandler(
    IAttemptRequestRepository requests, IExamRuntimeUnitOfWork unitOfWork, AttemptRequestDtoFactory dtos, Clock clock)
{
    /// <summary>Marks the request declined and saves.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="decidedByUserId">The signed-in staff user, taken from their token.</param>
    /// <param name="note">A reason the candidate will see; optional, at most 500 characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptRequestNotFoundError">No request has that id.</exception>
    /// <exception cref="AttemptRequestNotPendingError">The request was already decided.</exception>
    /// <exception cref="InvalidAttemptError">The note is too long.</exception>
    public async Task<AttemptRequestDto> HandleAsync(Guid requestId, Guid decidedByUserId, string? note, CancellationToken cancellationToken)
    {
        var request = await requests.GetByIdAsync(requestId, cancellationToken) ?? throw new AttemptRequestNotFoundError();

        request.Decline(decidedByUserId, clock.UtcNow, note);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await dtos.CreateAsync([request], cancellationToken))[0];
    }
}
