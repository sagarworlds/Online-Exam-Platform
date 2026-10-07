using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IDisputeRepository"/>.</summary>
public sealed class DisputeRepository(ExamRuntimeDbContext context) : IDisputeRepository
{
    /// <inheritdoc />
    public void Add(Dispute dispute) => context.Disputes.Add(dispute);

    /// <inheritdoc />
    public Task<Dispute?> GetByIdAsync(Guid disputeId, CancellationToken cancellationToken) =>
        context.Disputes.FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid attemptId, Guid questionId, CancellationToken cancellationToken) =>
        context.Disputes.AnyAsync(d => d.AttemptId == attemptId && d.QuestionId == questionId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Dispute>> ListForAttemptAsync(Guid attemptId, CancellationToken cancellationToken) =>
        await context.Disputes
            .AsNoTracking()
            .Where(d => d.AttemptId == attemptId)
            .OrderBy(d => d.RaisedAtUtc).ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Dispute>> ListAsync(DisputeStatus status, int take, CancellationToken cancellationToken) =>
        await context.Disputes
            .AsNoTracking()
            .Where(d => d.Status == status)
            .OrderBy(d => d.RaisedAtUtc).ThenBy(d => d.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Dispute>> ListOpenForQuestionAsync(Guid questionId, CancellationToken cancellationToken) =>
        await context.Disputes
            .Where(d => d.QuestionId == questionId && d.Status == DisputeStatus.Open)
            .ToListAsync(cancellationToken);
}
