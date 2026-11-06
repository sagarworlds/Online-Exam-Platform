using System.Collections.Concurrent;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Proves the Identity module's own registrations for the password-reset message: with the configuration Development ships it
/// resolves to the logging adapter, which logs a masked destination, and the link it builds opens the web app's reset page with the
/// code in the fragment, from the address the deployment configured. <see cref="ApiFactory"/> swaps in
/// <see cref="CapturingPasswordResetSender"/> for every other test, so without this the real registrations would never run.
/// </summary>
public sealed class PasswordResetDeliveryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Code = "3f2a9c1e-0b7d-4c55-9a60-1d2e3f4a5b6c:ABCDEF0123456789";
    private const string EscapedCode = "3f2a9c1e-0b7d-4c55-9a60-1d2e3f4a5b6c%3AABCDEF0123456789";

    [Fact]
    public async Task DevelopmentLogProvider_ResolvesTheLoggingSender_WhichLogsTheLinkForAMaskedDestination()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(logs))
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Identity:LinkBaseUrl"] = "https://exam.example" })));
        using var scope = host.Services.CreateScope();

        // The module's registration is the one ApiFactory later overrides, so look for it among all registrations.
        var sender = scope.ServiceProvider.GetServices<IPasswordResetSender>().OfType<LoggingPasswordResetSender>().Single();
        await sender.SendAsync("jane.doe@example.com", Code, TimeSpan.FromMinutes(30), CancellationToken.None);

        var entry = Assert.Single(logs.Entries, e => e.Category == typeof(LoggingPasswordResetSender).FullName);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("j***@example.com", entry.Message);
        Assert.DoesNotContain("jane.doe", entry.Message);
        Assert.Contains($"https://exam.example/password-reset/confirm#code={EscapedCode}", entry.Message);
    }

    [Theory]
    [InlineData("https://exam.example", "https://other.example", "https://exam.example")]
    [InlineData("https://exam.example/", "https://other.example", "https://exam.example")]
    [InlineData("", "https://other.example", "https://other.example")]
    [InlineData("   ", "https://other.example", "https://other.example")]
    public void Link_OpensTheResetPageOfTheConfiguredWebApp_WithTheCodeInTheFragment(string linkBase, string corsOrigin, string expectedBase)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:LinkBaseUrl"] = linkBase,
                ["Cors:AllowedOrigins:0"] = corsOrigin,
            })));
        using var scope = host.Services.CreateScope();

        var link = scope.ServiceProvider.GetRequiredService<IPasswordResetLinkBuilder>().Build(Code);

        // A fragment, not a query string: a browser never sends it to a server, so the code stays out of access logs and Referer headers.
        Assert.Equal($"{expectedBase}/password-reset/confirm#code={EscapedCode}", link);
        Assert.DoesNotContain('?', link);
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
    }
}
