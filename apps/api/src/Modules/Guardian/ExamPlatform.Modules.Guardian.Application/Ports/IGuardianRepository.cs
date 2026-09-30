using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.Application.Ports;

/// Repository port for Guardian aggregate.
public interface IGuardianRepository
{
    void Add(GuardianAggregate guardian);
    Task<GuardianAggregate?> GetByIdAsync(Guid guardianId, CancellationToken cancellationToken = default);
    Task<GuardianAggregate> GetByIdOrThrowAsync(Guid guardianId, CancellationToken cancellationToken = default);
    Task<GuardianAggregate?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GuardianAggregate>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default);
}
