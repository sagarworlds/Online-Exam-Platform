using System.Collections.Concurrent;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Proves that, with the configuration Development ships (<c>Identity:OtpDelivery:Provider</c>
/// is <c>DevelopmentLog</c>), the Identity module's own <see cref="IOtpSender"/> registration
/// resolves to <see cref="LoggingOtpSender"/>, and that the line it writes names a masked
/// destination, never the raw email address or phone number (NFR-6). <see cref="ApiFactory"/>
/// swaps in <see cref="CapturingOtpSender"/> for every other test, so without this the real
/// registration and the log line the fix is about would never run.
/// </summary>
public sealed class OtpDeliveryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(OtpChannel.Email, "jane.doe@example.com", "j***@example.com", "jane.doe")]
    [InlineData(OtpChannel.Sms, "+919876543210", "***********10", "+91987654")]
    public async Task DevelopmentLogProvider_ResolvesTheLoggingSender_WhichLogsOnlyAMaskedDestination(
        OtpChannel channel, string destination, string expectedMasked, string leakedFragment)
    {
        var logs = new CapturingLoggerProvider();
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var scope = host.Services.CreateScope();

        // The module's registration is the one ApiFactory later overrides, so look for it
        // among all registrations rather than taking the winning (test double) one.
        var sender = scope.ServiceProvider.GetServices<IOtpSender>().OfType<LoggingOtpSender>().Single();
        await sender.SendAsync(channel, destination, "123456", CancellationToken.None);

        var entry = Assert.Single(logs.Entries, e => e.Category == typeof(LoggingOtpSender).FullName);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(expectedMasked, entry.Message);
        Assert.DoesNotContain(destination, entry.Message);
        Assert.DoesNotContain(leakedFragment, entry.Message);

        // Still logged: signing in during local development depends on reading the code here.
        Assert.Contains("123456", entry.Message);
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
