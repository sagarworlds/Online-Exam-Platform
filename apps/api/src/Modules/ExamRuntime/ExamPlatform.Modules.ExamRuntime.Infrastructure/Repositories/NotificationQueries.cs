using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="INotificationQueries"/>: a few columns of what is still to be announced, and none of what was.</summary>
public sealed class NotificationQueries(ExamRuntimeDbContext context) : INotificationQueries
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SubmittedAttemptRow>> ListAwaitingResultNoticeAsync(DateTime submittedAfterUtc, CancellationToken cancellationToken) =>
        await (from attempt in context.Attempts.AsNoTracking()
               where attempt.Status == AttemptStatus.Submitted
                     && attempt.InvalidatedAtUtc == null
                     && attempt.SubmittedAtUtc > submittedAfterUtc
               where !context.NotificationDeliveries.Any(d =>
                   d.Kind == NotificationKind.ResultReleased
                   && d.SubjectId == attempt.Id
                   && d.RecipientId == attempt.CandidateId
                   && (d.SentAtUtc != null || d.Attempts >= NotificationDelivery.MaxAttempts))
               orderby attempt.SubmittedAtUtc
               select new SubmittedAttemptRow(attempt.Id, attempt.ExamId, attempt.CandidateId, attempt.Number, attempt.SubmittedAtUtc!.Value))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RevisionRow>> ListAwaitingRevisionNoticeAsync(DateTime revisedAfterUtc, CancellationToken cancellationToken) =>
        await (from revision in context.Set<AttemptResultRevision>().AsNoTracking()
               join attempt in context.Attempts.AsNoTracking() on revision.AttemptId equals attempt.Id
               where revision.RevisedAtUtc > revisedAfterUtc
                     && attempt.InvalidatedAtUtc == null
                     && attempt.SubmittedAtUtc != null
               where !context.NotificationDeliveries.Any(d =>
                   d.Kind == NotificationKind.ScoreRevised
                   && d.SubjectId == revision.Id
                   && d.RecipientId == attempt.CandidateId
                   && (d.SentAtUtc != null || d.Attempts >= NotificationDelivery.MaxAttempts))
               orderby revision.RevisedAtUtc
               select new RevisionRow(
                   revision.Id,
                   attempt.Id,
                   attempt.ExamId,
                   attempt.CandidateId,
                   attempt.SubmittedAtUtc!.Value,
                   revision.PreviousScore,
                   revision.PreviousMaxScore,
                   revision.NewScore,
                   revision.NewMaxScore,
                   revision.Reason,
                   revision.RevisedAtUtc))
            .ToListAsync(cancellationToken);
}
