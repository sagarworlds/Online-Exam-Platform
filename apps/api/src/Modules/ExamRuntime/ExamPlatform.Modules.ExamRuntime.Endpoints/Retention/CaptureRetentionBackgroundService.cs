using ExamPlatform.Modules.ExamRuntime.Application.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints.Retention;

/// <summary>
/// The host's timer for the capture sweep (FR-47): one sweep every <see cref="CaptureRetentionOptions.SweepMinutes"/> minutes while the host
/// is awake. A sweep that fails is logged and the timer carries on; the next sweep removes whatever is still too old.
/// </summary>
/// <remarks>Each sweep runs in its own scope, because the handler's dependencies are scoped. Only a count is logged: the rows name no one.</remarks>
public sealed class CaptureRetentionBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<CaptureRetentionOptions> options,
    ILogger<CaptureRetentionBackgroundService> logger) : BackgroundService
{
    // Long enough for start-up (migrations run before the host starts, but the database may still be waking) to be over.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(45);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("The capture sweep is off ({Section}:Enabled).", CaptureRetentionOptions.SectionName);
            return;
        }

        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.SweepMinutes));
            do
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "A capture sweep failed; the next one will try again.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var removed = await scope.ServiceProvider.GetRequiredService<PurgeOldCapturesHandler>().HandleAsync(cancellationToken);
        if (removed > 0)
            logger.LogInformation("Capture sweep removed {Count} webcam photos and screen pictures older than the retention period.", removed);
    }
}
