using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>
/// Writes the declared log retention into the log at every start, so the figure in force for any period of logs can be read
/// from the logs themselves. The startup check has already passed by the time this runs: a value below the minimum never gets here.
/// </summary>
/// <param name="options">The validated <c>LogRetention</c> settings.</param>
/// <param name="logger">Writes the record.</param>
public sealed class LogRetentionStartupReport(IOptions<LogRetentionOptions> options, ILogger<LogRetentionStartupReport> logger)
    : IHostedService
{
    /// <summary>Writes one Information line with the declared retention and its minimum.</summary>
    /// <param name="cancellationToken">Unused: the write is immediate.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Logs are declared to be kept for {RetentionDays} days (minimum {MinimumDays}, NFR-13). The log store keeps them; this application does not delete them.",
            options.Value.Days,
            LogRetentionOptions.MinimumDays);
        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
