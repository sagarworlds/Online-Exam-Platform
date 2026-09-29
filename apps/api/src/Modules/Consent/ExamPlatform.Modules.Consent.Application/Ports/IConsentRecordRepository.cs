using ExamPlatform.Modules.Consent.Domain;

namespace ExamPlatform.Modules.Consent.Application.Ports;

/// <summary>Persistence port for <see cref="ConsentRecord"/> aggregates.</summary>
public interface IConsentRecordRepository
{
    /// <summary>Loads a consent record by id, or null if none exists.</summary>
    /// <param name="id">The record's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ConsentRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads the current active (not withdrawn) consent record for a subject and purpose, if one exists.</summary>
    /// <param name="subjectId">Whose consent to look up.</param>
    /// <param name="purpose">What the consent must cover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ConsentRecord?> GetActiveAsync(Guid subjectId, ConsentPurpose purpose, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new consent record for insertion on the next unit-of-work commit.</summary>
    /// <param name="record">The record to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(ConsentRecord record, CancellationToken cancellationToken);
}
