using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Shows staff where an attempt was sat from: its starting address and device, and each change (FR-26).</summary>
public sealed class ListAttemptClientsHandler(StaffAttemptAccess access)
{
    /// <summary>Lists the attempt's sightings, oldest first.</summary>
    /// <param name="examId">The exam named in the route.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is an attempt at another exam.</exception>
    public async Task<IReadOnlyList<AttemptClientDto>> HandleAsync(Guid examId, Guid attemptId, CancellationToken cancellationToken)
    {
        var (attempt, _) = await access.LoadAsync(examId, attemptId, cancellationToken);

        return attempt.ClientSightings
            .OrderBy(s => s.SeenAtUtc)
            .Select(s => new AttemptClientDto(s.IpAddress, s.DeviceFingerprint, s.SeenAtUtc, s.Reason))
            .ToList();
    }
}
