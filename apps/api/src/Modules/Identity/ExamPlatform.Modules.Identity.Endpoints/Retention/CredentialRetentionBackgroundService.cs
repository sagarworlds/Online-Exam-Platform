using ExamPlatform.Modules.Identity.Application.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Identity.Endpoints.Retention;

/// <summary>
/// The host's timer for the credential sweep (FR-47): one sweep every <see cref="CredentialRetentionOptions.SweepMinutes"/> minutes while the
/// host is awake. A sweep that fails is logged and the timer carries on; the next sweep removes whatever is still expired.
/// </summary>
/// <remarks>
/// Each sweep runs in its own scope, because the handler's dependencies are scoped and a hosted service lives for the whole process.
/// Only counts are logged. The rows removed name no one, and logging them would copy the personal data this sweep exists to remove.
/// </remarks>
public sealed class CredentialRetentionBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<CredentialRetentionOptions> options,
    ILogger<CredentialRetentionBackgroundService> logger) : BackgroundService
{
    // Long enough for start-up (migrations run before the host starts, but the database may still be waking) to be over.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("The credential sweep is off (Identity:Retention:Enabled).");
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
                    logger.LogError(exception, "A credential sweep failed; the next one will try again.");
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
        var result = await scope.ServiceProvider.GetRequiredService<PurgeExpiredCredentialsHandler>().HandleAsync(cancellationToken);
        if (result.IsEmpty)
            return;

        logger.LogInformation(
            "Credential sweep removed {OneTimeCodes} expired one-time codes, {PasswordResetTokens} expired password-reset links and {Sessions} expired sessions.",
            result.OneTimeCodes, result.PasswordResetTokens, result.Sessions);
    }
}
