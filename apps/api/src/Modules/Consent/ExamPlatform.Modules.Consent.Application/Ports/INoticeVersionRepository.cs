using ExamPlatform.Modules.Consent.Domain;

namespace ExamPlatform.Modules.Consent.Application.Ports;

/// <summary>Persistence port for <see cref="NoticeVersion"/> reference data.</summary>
public interface INoticeVersionRepository
{
    /// <summary>Loads a notice version by id, or null if none exists.</summary>
    /// <param name="id">The notice version's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NoticeVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
