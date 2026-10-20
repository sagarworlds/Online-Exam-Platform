using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Application.Retention;

/// <summary>Removes webcam photos and screen pictures older than the configured retention period (FR-47).</summary>
/// <remarks>Only the images go. The attempt, its answers and its score are kept; they follow the account's own rule in the data map.</remarks>
public sealed class PurgeOldCapturesHandler(IWebcamPhotoRepository photos, Clock clock, IOptions<CaptureRetentionOptions> options)
{
    /// <summary>Deletes every capture taken before the cut-off.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many captures were removed.</returns>
    public Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var cutoffUtc = clock.UtcNow.AddDays(-options.Value.RetentionDays);
        return photos.DeleteCapturedBeforeAsync(cutoffUtc, cancellationToken);
    }
}
