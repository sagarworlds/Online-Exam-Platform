using ExamPlatform.SharedKernel.Infrastructure.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The database readiness check (NFR-3). These cases never reach a database: a missing connection string is caught before any
/// connection, and a loopback port with nothing listening refuses the connection at once.
/// </summary>
public class DatabaseReadinessCheckTests
{
    private static DatabaseReadinessCheck CheckWith(string? connectionString)
    {
        var settings = connectionString is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connectionString };
        return new DatabaseReadinessCheck(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    [Fact]
    public async Task NoConnectionString_IsUnhealthy_AndSaysWhatIsMissing()
    {
        var result = await CheckWith(connectionString: null).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("ConnectionStrings:Postgres", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableDatabase_IsUnhealthy_WithTheCause_AndNotThePassword()
    {
        const string connectionString = "Host=127.0.0.1;Port=1;Database=examplatform;Username=examplatform;Password=canary-value-1;Timeout=1";

        var result = await CheckWith(connectionString).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
        Assert.DoesNotContain("canary-value-1", result.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACallerWhoGivesUp_IsNotReportedAsAnUnhealthyDatabase()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var check = CheckWith("Host=127.0.0.1;Port=1;Database=examplatform;Username=examplatform;Password=unused;Timeout=1");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.CheckHealthAsync(new HealthCheckContext(), cancelled.Token));
    }
}
