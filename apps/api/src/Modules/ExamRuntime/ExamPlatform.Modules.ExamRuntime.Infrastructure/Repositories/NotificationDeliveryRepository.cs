using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="INotificationDeliveryRepository"/>.</summary>
public sealed class NotificationDeliveryRepository(ExamRuntimeDbContext context) : INotificationDeliveryRepository
{
    /// <inheritdoc />
    public void Add(NotificationDelivery delivery) => context.NotificationDeliveries.Add(delivery);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationDelivery>> ListAsync(
        NotificationKind kind, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken) =>
        await context.NotificationDeliveries
            .Where(d => d.Kind == kind && subjectIds.Contains(d.SubjectId))
            .ToListAsync(cancellationToken);
}
