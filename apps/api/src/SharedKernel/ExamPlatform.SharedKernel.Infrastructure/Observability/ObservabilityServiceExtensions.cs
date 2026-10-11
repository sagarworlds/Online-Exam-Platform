using ExamPlatform.SharedKernel.Infrastructure.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>Registers the logging, retention and health-check services of the observability story (NFR-9, NFR-13).</summary>
public static class ObservabilityServiceExtensions
{
    /// <summary>The log category ASP.NET Core writes its per-request lines in.</summary>
    public const string FrameworkRequestLogCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    /// <summary>
    /// Writes the console provider's lines with <see cref="RedactingJsonConsoleFormatter"/>. It selects the formatter on the console
    /// provider the host already has, and adds no other provider, so providers registered elsewhere (for example a test's log
    /// capture) keep working and no line is written twice.
    /// </summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ILoggingBuilder AddExamPlatformJsonConsole(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ConsoleFormatter, RedactingJsonConsoleFormatter>();
        builder.AddConsole(options => options.FormatterName = RedactingJsonConsoleFormatter.FormatterName);

        // The framework writes its own request lines in this category, and they carry the query string. The request line of
        // RequestLoggingMiddleware replaces them, so they stay at Warning whatever the Logging configuration says: this rule is
        // more specific than the configured "Microsoft.AspNetCore" level, and the more specific rule wins.
        builder.AddFilter(FrameworkRequestLogCategory, LogLevel.Warning);
        return builder;
    }

    /// <summary>
    /// Registers the log retention check (NFR-13), a startup report of it, and the readiness check for the database.
    /// </summary>
    /// <remarks>
    /// The retention check runs when the host starts, not here: <c>ValidateOnStart</c> throws an
    /// <see cref="OptionsValidationException"/> then, so a host with <c>LogRetention:Days</c> below 180 refuses to serve.
    /// </remarks>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configuration">Supplies the <c>LogRetention</c> section and the <c>Postgres</c> connection string.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddExamPlatformObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LogRetentionOptions>()
            .Bind(configuration.GetSection(LogRetentionOptions.SectionName))
            .Validate(options => options.Days >= LogRetentionOptions.MinimumDays, LogRetentionOptions.BelowMinimumMessage)
            .ValidateOnStart();
        services.AddHostedService<LogRetentionStartupReport>();

        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessCheck>(DatabaseReadinessCheck.Name, tags: [DatabaseReadinessCheck.ReadyTag]);
        return services;
    }
}
