using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain.DataRequests;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure.Repositories;

/// <summary>The <see cref="IDataRequestRepository"/> over the Identity database.</summary>
public sealed class DataRequestRepository(IdentityDbContext context) : IDataRequestRepository
{
    /// <inheritdoc />
    public Task<DataRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.DataRequests.FirstOrDefaultAsync(request => request.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasOpenAsync(Guid userId, DataRequestKind kind, CancellationToken cancellationToken) =>
        context.DataRequests.AnyAsync(
            request => request.UserId == userId && request.Kind == kind && request.Status == DataRequestStatus.Received,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataRequest>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.DataRequests
            .Where(request => request.UserId == userId)
            .OrderByDescending(request => request.ReceivedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataRequest>> ListOpenAsync(CancellationToken cancellationToken) =>
        await context.DataRequests
            .Where(request => request.Status == DataRequestStatus.Received)
            .OrderBy(request => request.DueAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(DataRequest request, CancellationToken cancellationToken) =>
        await context.DataRequests.AddAsync(request, cancellationToken);
}
