using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.Application.Ports;

/// Repository port for Guardian aggregate.
public interface IGuardianRepository
{
    void Add(GuardianAggregate guardian);

    /// <summary>Finds a guardian with their candidate links, ready to change; null when there is none.</summary>
    Task<GuardianAggregate?> GetByIdAsync(Guid guardianId, CancellationToken cancellationToken = default);
    Task<GuardianAggregate?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GuardianAggregate>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the guardian whose link carries this confirmation-code hash, with their links ready to change; null when there is none.
    /// </summary>
    Task<GuardianAggregate?> GetByVerificationTokenHashAsync(string verificationTokenHash, CancellationToken cancellationToken = default);
}
