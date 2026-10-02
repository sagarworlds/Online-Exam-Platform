using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Reads an attempt: the questions while it is open, the result once it is over.</summary>
public sealed class GetAttemptHandler(AttemptAccess access, AttemptViewBuilder views)
{
    /// <summary>Returns one of the candidate's own attempts, closing it first if its time has run out.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    public async Task<AttemptDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);
        return await views.BuildAsync(attempt, exam, cancellationToken);
    }
}
