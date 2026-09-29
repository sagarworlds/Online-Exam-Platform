using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Consent.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IConsentRecordRepository"/>.</summary>
public sealed class ConsentRecordRepository(ConsentDbContext context) : IConsentRecordRepository
{
    /// <inheritdoc />
    public Task<ConsentRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.ConsentRecords.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<ConsentRecord?> GetActiveAsync(Guid subjectId, ConsentPurpose purpose, CancellationToken cancellationToken) =>
        context.ConsentRecords.FirstOrDefaultAsync(
            r => r.SubjectId == subjectId && r.Purpose == purpose && r.WithdrawnAtUtc == null, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(ConsentRecord record, CancellationToken cancellationToken) =>
        await context.ConsentRecords.AddAsync(record, cancellationToken);
}
