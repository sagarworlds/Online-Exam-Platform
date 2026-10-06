using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>
/// The exam page's heartbeat (FR-29): whether the attempt is still open, paused, and what it has been warned about, without the
/// questions, so the page can ask every few seconds and learn of an administrator's action promptly.
/// </summary>
public sealed class GetAttemptStatusHandler(AttemptAccess access, Clock clock, IClientInfo clientInfo, IExamRuntimeUnitOfWork unitOfWork)
{
    /// <summary>Returns the status, closing the attempt first if its time has run out.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    public async Task<AttemptStatusDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken)
    {
        var (attempt, _) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        // The heartbeat is the call a second device makes within seconds of taking over, so it is where a change is noticed (FR-26).
        if (attempt.NoteClient(clientInfo.IpAddress, clientInfo.DeviceFingerprint, clock.UtcNow))
            await unitOfWork.SaveChangesAsync(cancellationToken);


        return new AttemptStatusDto(
            attempt.Status,
            attempt.PausedAtUtc,
            attempt.DeadlineUtc,
            clock.UtcNow,
            attempt.Warnings.OrderBy(w => w.IssuedAtUtc).Select(w => new AttemptWarningDto(w.Id, w.Message, w.IssuedAtUtc)).ToList());
    }
}
