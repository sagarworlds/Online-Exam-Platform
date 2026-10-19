using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IWebcamPhotoRepository"/>.</summary>
public sealed class WebcamPhotoRepository(ExamRuntimeDbContext context) : IWebcamPhotoRepository
{
    /// <inheritdoc />
    public void Add(AttemptWebcamPhoto photo) => context.WebcamPhotos.Add(photo);

    /// <inheritdoc />
    public Task<int> CountAsync(Guid attemptId, CaptureKind kind, CancellationToken cancellationToken) =>
        context.WebcamPhotos.CountAsync(p => p.AttemptId == attemptId && p.Kind == kind, cancellationToken);

    /// <inheritdoc />
    public async Task<DateTime?> LatestCapturedAtAsync(Guid attemptId, CaptureKind kind, CancellationToken cancellationToken) =>
        await context.WebcamPhotos
            .Where(p => p.AttemptId == attemptId && p.Kind == kind)
            .MaxAsync(p => (DateTime?)p.CapturedAtUtc, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebcamPhotoSummary>> ListAsync(Guid attemptId, CancellationToken cancellationToken) =>
        await context.WebcamPhotos
            .AsNoTracking()
            .Where(p => p.AttemptId == attemptId)
            .OrderBy(p => p.CapturedAtUtc).ThenBy(p => p.Id)
            .Select(p => new WebcamPhotoSummary(p.Id, p.Kind, p.CapturedAtUtc, p.Image.Length))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<AttemptWebcamPhoto?> GetAsync(Guid attemptId, Guid photoId, CancellationToken cancellationToken) =>
        context.WebcamPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.AttemptId == attemptId && p.Id == photoId, cancellationToken);
}
