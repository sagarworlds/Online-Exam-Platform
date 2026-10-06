using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Reads an attempt: the questions while it is open, the result once it is over.</summary>
public sealed class GetAttemptHandler(AttemptAccess access, AttemptViewBuilder views, IClientInfo clientInfo, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Returns one of the candidate's own attempts, closing it first if its time has run out.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    public async Task<AttemptDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        // Loading the page again from another address or device is a change of place worth keeping (FR-26).
        if (attempt.NoteClient(clientInfo.IpAddress, clientInfo.DeviceFingerprint, clock.UtcNow))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return await views.BuildAsync(attempt, exam, cancellationToken);
    }
}
