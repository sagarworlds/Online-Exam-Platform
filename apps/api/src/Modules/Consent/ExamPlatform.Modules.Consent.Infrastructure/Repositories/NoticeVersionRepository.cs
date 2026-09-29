using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Consent.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="INoticeVersionRepository"/>.</summary>
public sealed class NoticeVersionRepository(ConsentDbContext context) : INoticeVersionRepository
{
    /// <inheritdoc />
    public Task<NoticeVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.NoticeVersions.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
}
