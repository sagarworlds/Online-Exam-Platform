using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Counts the SQL commands EF Core runs, and keeps their text on request, so a test can say how many round trips a request takes.
/// A host in the Development environment logs every command at Information; this listens for exactly that event.
/// </summary>
public sealed class SqlCommandCounter : ILoggerProvider
{
    /// <summary>RelationalEventId.CommandExecuted.</summary>
    private const int CommandExecuted = 20101;

    private int _count;
    private volatile bool _keepText;
    private readonly List<string> _statements = [];

    /// <summary>How many commands have run since the last <see cref="Reset"/>.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Starts counting from zero.</summary>
    /// <param name="keepText">Whether to keep each command's text, for a message that says what was run.</param>
    public void Reset(bool keepText = false)
    {
        lock (_statements)
        {
            _statements.Clear();
        }

        Interlocked.Exchange(ref _count, 0);
        _keepText = keepText;
    }

    /// <summary>The commands kept since the last <see cref="Reset"/>, each cut to its first 300 characters.</summary>
    public IReadOnlyList<string> Statements()
    {
        lock (_statements)
        {
            return [.. _statements];
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        categoryName == "Microsoft.EntityFrameworkCore.Database.Command" ? new CommandLogger(this) : NullLogger.Instance;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class CommandLogger(SqlCommandCounter owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != CommandExecuted)
            {
                return;
            }

            Interlocked.Increment(ref owner._count);
            if (!owner._keepText)
            {
                return;
            }

            var text = formatter(state, exception).Replace("\r", " ").Replace("\n", " ");
            lock (owner._statements)
            {
                owner._statements.Add(text.Length > 300 ? text[..300] : text);
            }
        }
    }
}

/// <summary>The API host with a <see cref="SqlCommandCounter"/> listening to every command it runs.</summary>
public sealed class SqlCountingApiFactory : ApiFactory
{
    /// <summary>The counter; reset it before the request being measured.</summary>
    public SqlCommandCounter Sql { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(Sql);
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
        });
    }
}
