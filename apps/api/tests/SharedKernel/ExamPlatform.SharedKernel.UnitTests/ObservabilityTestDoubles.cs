using Microsoft.Extensions.Logging;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>An <see cref="ILogger{TCategory}"/> that keeps what it was asked to write, and the scopes that were opened.</summary>
/// <typeparam name="TCategory">The category the logger is for.</typeparam>
internal sealed class CapturingLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>One log call, rendered.</summary>
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    /// <summary>Every log call, in order.</summary>
    public List<Entry> Entries { get; } = [];

    /// <summary>The state of every scope opened, in order.</summary>
    public List<object> Scopes { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        Scopes.Add(state);
        return null;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}

/// <summary>An <see cref="Microsoft.Extensions.Logging.IExternalScopeProvider"/> that reports a fixed set of scopes.</summary>
/// <param name="scopes">The scopes to report, in order.</param>
internal sealed class FixedScopes(params object[] scopes) : Microsoft.Extensions.Logging.IExternalScopeProvider
{
    /// <inheritdoc />
    public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
    {
        foreach (var scope in scopes)
        {
            callback(scope, state);
        }
    }

    /// <inheritdoc />
    public IDisposable Push(object? state) => throw new NotSupportedException("Scopes are fixed in this test.");
}
