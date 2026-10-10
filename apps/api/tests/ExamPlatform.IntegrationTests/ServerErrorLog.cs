using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Keeps the most recent server-side errors logged by the test hosts, so a request that fails with a 500 says why in the test output.
/// </summary>
/// <remarks>
/// A 500 from the API is otherwise visible only as its status code: the exception is logged on the server and never reaches the test.
/// The log is shared by every test host in the process, so a line may come from another suite running at the same time. That is
/// acceptable for diagnosis; the entries carry their category and exception, which is enough to tell them apart.
/// </remarks>
internal static class ServerErrorLog
{
    private const int Capacity = 20;
    private static readonly ConcurrentQueue<string> Recent = new();

    /// <summary>Records one error line, dropping the oldest once the log is full.</summary>
    /// <param name="entry">The category, message and exception of the logged error.</param>
    public static void Add(string entry)
    {
        Recent.Enqueue(entry);
        while (Recent.Count > Capacity && Recent.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Throws when the response is not a success, and says what the server answered and which errors it logged around the same time.
    /// </summary>
    /// <param name="response">The response to check.</param>
    /// <exception cref="HttpRequestException">The response has a non-success status; the message carries the body and recent server errors.</exception>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        var errors = string.Join(Environment.NewLine + "---" + Environment.NewLine, Recent.ToArray());
        throw new HttpRequestException(
            $"{(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}{Environment.NewLine}Recent server errors:{Environment.NewLine}{errors}",
            null,
            (HttpStatusCode)response.StatusCode);
    }
}

/// <summary>Sends the error-level log entries of a test host to <see cref="ServerErrorLog"/>; other levels are left to the host's own providers.</summary>
internal sealed class ServerErrorLogProvider : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class Logger(string category) : ILogger
    {
        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var detail = exception is null ? string.Empty : Environment.NewLine + exception;
            ServerErrorLog.Add($"{category}: {formatter(state, exception)}{detail}");
        }
    }
}
