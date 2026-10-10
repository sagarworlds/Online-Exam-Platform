using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints.Notifications;

/// <summary>
/// Runs one pass of the notification run (<see cref="NotificationRun"/>) in a scope of its own and with the database's say-so that no
/// other pass is running anywhere. It is started by the host's timer and by the key-protected route, which may both be in use, and
/// by more than one instance of the API; the lock is what keeps two of them from working through the same due messages together.
/// </summary>
public sealed class NotificationRunner(IServiceScopeFactory scopes, ILogger<NotificationRunner> logger)
{
    // An arbitrary number the whole platform agrees on for this one lock.
    private const long LockKey = 0x4E4F_5449_4659_0001;

    /// <summary>Runs a pass unless one is already running.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the pass did, or <see langword="null"/> when another pass holds the lock and this one stood down.</returns>
    public async Task<NotificationRunSummary?> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var connectionString = provider.GetRequiredService<ExamRuntimeDbContext>().Database.GetConnectionString();

        // The lock lives on a connection of its own, for as long as a transaction on it is open: a lock tied to a transaction is the kind
        // that holds through a pooled database connection (Neon's pooler hands one server connection to whoever is next between
        // transactions), where one tied to a session could be given to someone else.
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);
        await using var lockTransaction = await lockConnection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", lockConnection, lockTransaction))
        {
            command.Parameters.AddWithValue("key", LockKey);
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            {
                logger.LogInformation("Notification pass skipped: another pass is running.");
                return null;
            }
        }

        var summary = await provider.GetRequiredService<NotificationRun>().RunAsync(
            provider.GetRequiredService<Clock>().UtcNow,
            cancellationToken,
            exception => logger.LogError(exception, "A part of the notification pass could not be read and was skipped."));

        if (!summary.MailAvailable)
        {
            // Not an error: the feed is filled without a mail server, and only the e-mails are skipped.
            logger.LogDebug("Notification pass sent no e-mail: no mail server is configured. The in-app feed was still filled.");
        }
        else if (summary is { RemindersSent: 0, ResultNoticesSent: 0, RevisionNoticesSent: 0, NotSent: 0, Errors: 0 })
        {
            logger.LogDebug("Notification pass found nothing due.");
        }
        else
        {
            logger.LogInformation(
                "Notification pass sent {Reminders} reminders, {Results} result notices and {Revisions} revision notices; {NotSent} not taken by the mail server, {Errors} errors.",
                summary.RemindersSent, summary.ResultNoticesSent, summary.RevisionNoticesSent, summary.NotSent, summary.Errors);
        }

        return summary;
    }
}
