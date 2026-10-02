using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Lets an administrator give one candidate one more attempt at an exam, on request.</summary>
public sealed class GrantExtraAttemptHandler(
    IExamCatalog catalog,
    IExamRoster roster,
    IAttemptRepository attempts,
    IExtraAttemptGrantRepository grants,
    IExamRuntimeUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Grants the attempt and returns how the candidate now stands.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate who asked for another attempt.</param>
    /// <param name="grantedByUserId">The signed-in staff user, taken from their token and never from the request.</param>
    /// <param name="reason">Why, in their words; optional.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="CandidateNotEnrolledError">The person never accepted an invitation to the exam.</exception>
    /// <exception cref="ExamClosedError">Nobody can start the exam any more, so an attempt granted now could never be used.</exception>
    /// <exception cref="AttemptAvailableError">The candidate still has an attempt they have not used.</exception>
    /// <exception cref="InvalidAttemptError">The reason is too long.</exception>
    /// <exception cref="ConcurrencyConflictError">Another administrator granted an attempt to the same candidate at the same moment.</exception>
    public async Task<ExamCandidateDto> HandleAsync(
        Guid examId, Guid candidateId, Guid grantedByUserId, string? reason, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamNotFoundError();

        var candidate = (await roster.GetEnrolledCandidatesAsync(examId, cancellationToken)).FirstOrDefault(c => c.UserId == candidateId)
            ?? throw new CandidateNotEnrolledError();

        var nowUtc = clock.UtcNow;
        if (ExamCandidateRows.IsWindowClosed(exam, nowUtc))
            throw new ExamClosedError();

        var theirs = await attempts.ListForCandidateAtExamAsync(examId, candidateId, cancellationToken);
        var granted = await grants.CountAsync(examId, candidateId, cancellationToken);
        if (!AttemptAllowance.CanGrant(theirs.Count, granted))
            throw new AttemptAvailableError();

        // Numbered one after the last, and unique in the store, so two administrators acting at once cannot both succeed.
        grants.Add(ExtraAttemptGrant.Create(examId, candidateId, granted + 1, grantedByUserId, nowUtc, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ExamCandidateRows.For(exam, candidate, theirs, granted + 1, nowUtc);
    }
}
