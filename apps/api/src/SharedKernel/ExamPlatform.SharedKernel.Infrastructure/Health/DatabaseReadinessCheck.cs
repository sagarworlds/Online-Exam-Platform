using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ExamPlatform.SharedKernel.Infrastructure.Health;

/// <summary>
/// The readiness check for the database (NFR-3): it opens a connection to the platform's Postgres database and runs a trivial
/// query. Every module's schema lives in that one database, so one check covers them all. A replica that cannot reach it is
/// not ready for traffic, but it stays alive: the liveness check does not depend on this.
/// </summary>
/// <remarks>
/// The check reports failure as a result, not as an exception, so the health service logs it with its reason and a caller sees
/// 503. The result names the failure without the connection string, which holds the password. A check that hangs would hold up
/// the probe, so the query has a short deadline and a probe that misses it reports the database as unavailable.
/// </remarks>
/// <param name="configuration">Supplies the <c>Postgres</c> connection string.</param>
public sealed class DatabaseReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    /// <summary>The name the check is registered under, and so reported as.</summary>
    public const string Name = "database";

    /// <summary>The tag that makes a check part of the readiness probe and not of the liveness probe.</summary>
    public const string ReadyTag = "ready";

    /// <summary>The connection string's name in the <c>ConnectionStrings</c> section, the same one every module reads.</summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>
    /// How long one probe may take. Short enough that a probe answers before an orchestrator gives up on it, long enough for a
    /// healthy database on a warm connection pool.
    /// </summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    /// <summary>Opens a connection and runs <c>SELECT 1</c>.</summary>
    /// <param name="context">The health check context (unused).</param>
    /// <param name="cancellationToken">Cancelled when the caller gives up on the probe.</param>
    /// <returns>Healthy when the query answers; Unhealthy with the reason otherwise.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // A caller that has given up is not a verdict on the database, so it is never reported as Unhealthy.
        cancellationToken.ThrowIfCancellationRequested();

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy($"No connection string is configured under ConnectionStrings:{ConnectionStringName}.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(deadline.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(deadline.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The probe's own deadline lands here too: a database that does not answer in time is not ready.
            return HealthCheckResult.Unhealthy("The database did not answer a readiness query.", exception);
        }
    }
}
