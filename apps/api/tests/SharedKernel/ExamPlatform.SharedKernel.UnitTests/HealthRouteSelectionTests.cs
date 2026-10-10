using ExamPlatform.SharedKernel.Infrastructure.Health;
using ExamPlatform.SharedKernel.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// Which checks each health route runs. The database check is registered for readiness only, so <c>/v1/health</c> (Render's check)
/// and liveness must never run it: a database outage must not take the service out of rotation.
/// </summary>
public class HealthRouteSelectionTests
{
    [Fact]
    public async Task TheOriginalAndLivenessRoutes_RunNoChecks_AndReadinessRunsTheDatabaseCheck()
    {
        // No connection string is configured, so the database check reports Unhealthy without opening a connection.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [$"{LogRetentionOptions.SectionName}:Days"] = "180" }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddExamPlatformObservability(configuration);
        using var provider = services.BuildServiceProvider();
        var health = provider.GetRequiredService<HealthCheckService>();

        var original = await health.CheckHealthAsync(registration => ObservabilityPipelineExtensions.SelectsNoChecks(registration));
        var readiness = await health.CheckHealthAsync(registration => ObservabilityPipelineExtensions.SelectsReadinessChecks(registration));

        Assert.Empty(original.Entries);
        Assert.Equal(HealthStatus.Healthy, original.Status);
        Assert.Contains(DatabaseReadinessCheck.Name, readiness.Entries.Keys);
        Assert.Equal(HealthStatus.Unhealthy, readiness.Status);
    }

    [Fact]
    public void OnlyTheReadyTag_SelectsADatabaseCheck_ForReadiness()
    {
        var database = new HealthCheckRegistration(
            DatabaseReadinessCheck.Name, new DatabaseReadinessCheck(new ConfigurationBuilder().Build()), null, new[] { DatabaseReadinessCheck.ReadyTag });
        var untagged = new HealthCheckRegistration(
            "other", new DatabaseReadinessCheck(new ConfigurationBuilder().Build()), null, Array.Empty<string>());

        Assert.True(ObservabilityPipelineExtensions.SelectsReadinessChecks(database));
        Assert.False(ObservabilityPipelineExtensions.SelectsReadinessChecks(untagged));
        Assert.False(ObservabilityPipelineExtensions.SelectsNoChecks(database));
    }
}
