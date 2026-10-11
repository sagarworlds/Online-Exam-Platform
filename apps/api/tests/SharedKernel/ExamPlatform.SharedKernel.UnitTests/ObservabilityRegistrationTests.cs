using System.Text.Json;
using ExamPlatform.SharedKernel.Infrastructure.Health;
using ExamPlatform.SharedKernel.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The registration of the observability services: the log retention floor (NFR-13) is refused below 180 days, the committed
/// configuration meets it, and the console provider is wired to the redacting formatter.
/// </summary>
public class ObservabilityRegistrationTests
{
    /// <summary>The options as the host would resolve them, with the given <c>LogRetention:Days</c> (or none at all).</summary>
    private static IServiceProvider ServicesWithRetention(string? days)
    {
        var settings = days is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { [$"{LogRetentionOptions.SectionName}:Days"] = days };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddExamPlatformObservability(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("179")]
    [InlineData("1")]
    [InlineData("0")]
    public void RetentionBelowTheMinimum_IsRefused_WithTheMinimumNamed(string days)
    {
        var provider = ServicesWithRetention(days);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<LogRetentionOptions>>().Value);
        Assert.Contains("180", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetentionThatIsMissing_IsRefused()
    {
        var provider = ServicesWithRetention(days: null);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<LogRetentionOptions>>().Value);
    }

    [Theory]
    [InlineData(180)]
    [InlineData(365)]
    [InlineData(2555)]
    public void RetentionAtOrAboveTheMinimum_IsAccepted(int days)
    {
        var provider = ServicesWithRetention(days.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(days, provider.GetRequiredService<IOptions<LogRetentionOptions>>().Value.Days);
    }

    [Fact]
    public void TheMinimum_IsTheFigureNfr13Records()
    {
        Assert.Equal(180, LogRetentionOptions.MinimumDays);
    }

    [Fact]
    public void TheCommittedApiSettings_DeclareAtLeastTheMinimum()
    {
        var path = Path.Combine(FindRepositoryApiRoot(), "src", "Host", "ExamPlatform.Api", "appsettings.json");

        Assert.True(File.Exists(path), $"Expected the API's settings at {path}.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var days = document.RootElement.GetProperty(LogRetentionOptions.SectionName).GetProperty("Days").GetInt32();

        Assert.True(
            days >= LogRetentionOptions.MinimumDays,
            $"appsettings.json declares {days} days of log retention; NFR-13 requires at least {LogRetentionOptions.MinimumDays}.");
    }

    [Fact]
    public void TheConsoleProvider_IsWiredToTheRedactingFormatter()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddExamPlatformJsonConsole());

        using var provider = services.BuildServiceProvider();
        Assert.Contains(provider.GetServices<ConsoleFormatter>(), formatter => formatter is RedactingJsonConsoleFormatter);

        // Writing through the logger must work end to end (the line itself goes to the console, and is not asserted here).
        provider.GetRequiredService<ILoggerFactory>().CreateLogger("ExamPlatform.Test").LogWarning("wiring check");
    }

    [Fact]
    public void AddingTheFormatter_OnAHostWithAConsoleProvider_DoesNotAddASecondOne()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddExamPlatformJsonConsole();
        });

        using var provider = services.BuildServiceProvider();
        var consoleProviders = provider.GetServices<ILoggerProvider>().Where(p => p.GetType().Name == "ConsoleLoggerProvider").ToList();
        Assert.Single(consoleProviders);
    }

    [Fact]
    public void AProviderAddedElsewhere_SurvivesTheFormatterSetup()
    {
        var capture = new NoopLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(capture);
            builder.AddExamPlatformJsonConsole();
        });

        using var provider = services.BuildServiceProvider();
        Assert.Contains(provider.GetServices<ILoggerProvider>(), p => ReferenceEquals(p, capture));
    }

    private sealed class NoopLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void TheFrameworksRequestLines_AreKeptAtWarning_WhateverTheConfiguredLevel()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddExamPlatformJsonConsole());

        var filter = services.BuildServiceProvider().GetRequiredService<IOptions<LoggerFilterOptions>>().Value;

        Assert.Contains(filter.Rules, rule =>
            rule.CategoryName == ObservabilityServiceExtensions.FrameworkRequestLogCategory && rule.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public void TheDatabaseCheck_IsRegisteredUnderTheReadyTag()
    {
        var services = new ServiceCollection();
        services.AddExamPlatformObservability(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [$"{LogRetentionOptions.SectionName}:Days"] = "180" }).Build());

        var registration = services.BuildServiceProvider()
            .GetRequiredService<IOptions<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>()
            .Value.Registrations.Single();

        Assert.Equal(DatabaseReadinessCheck.Name, registration.Name);
        Assert.Contains(DatabaseReadinessCheck.ReadyTag, registration.Tags);
    }

    /// <summary>Walks up from the test binary to the folder holding the solution file (<c>apps/api</c>).</summary>
    private static string FindRepositoryApiRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExamPlatform.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The solution folder (containing ExamPlatform.slnx) was not found above the test binary.");
    }
}
