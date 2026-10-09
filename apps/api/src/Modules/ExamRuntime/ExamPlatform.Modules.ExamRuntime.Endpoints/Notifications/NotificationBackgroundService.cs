using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints.Notifications;

/// <summary>
/// The host's own timer for the notification run (FR-39): a pass every <see cref="NotificationOptions.PollMinutes"/> minutes while the host
/// is awake. A host that sleeps when idle will miss the passes it sleeps through, so the key-protected route exists for an outside
/// scheduler to start one and wake it. A pass that fails is logged and the timer carries on.
/// </summary>
public sealed class NotificationBackgroundService(
    NotificationRunner runner, IOptions<NotificationOptions> options, ILogger<NotificationBackgroundService> logger) : BackgroundService
{
    // Long enough for start-up (migrations run before the host starts, but the database may still be waking) to be over.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("The notification timer is off (Notifications:Enabled).");
            return;
        }

        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.PollMinutes));
            do
            {
                try
                {
                    await runner.RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "A notification pass failed; the next one will try again.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }
}
