using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>What the development terminal says when a sign-in gets no code.</summary>
public class LoggingSignInDiagnosticsTests
{
    private sealed class CollectingLogger : ILogger<LoggingSignInDiagnostics>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void ItSaysWhyNoCodeWasSent_AsAWarningThatStandsOut_WithoutTheRawAddress()
    {
        var logger = new CollectingLogger();

        new LoggingSignInDiagnostics(logger).Explain(SignInHint.NoAccountForAddress, OtpChannel.Email, "candidate@example.com");

        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.StartsWith("[DEV ONLY", message);
        Assert.Contains("no account has this address", message);
        // Masked, like the codes the development log prints, so the log never holds a raw address.
        Assert.DoesNotContain("candidate@example.com", message);
        Assert.Contains("c***@example.com", message);
    }

    [Theory]
    [InlineData(SignInHint.NoAccountForAddress, "Register first")]
    [InlineData(SignInHint.AccountLocked, "suspended or deactivated")]
    [InlineData(SignInHint.StaffMustUsePasswordAndCode, "Password tab")]
    [InlineData(SignInHint.CandidateHasNoPassword, "One-time code tab")]
    [InlineData(SignInHint.WrongPassword, "password is wrong")]
    public void EveryReason_SaysWhatToDoAboutIt(SignInHint hint, string advice)
    {
        Assert.Contains(advice, LoggingSignInDiagnostics.ReasonFor(hint));
    }

    [Fact]
    public void TheDiagnosticsOfEveryOtherDelivery_SayNothingAtAll()
    {
        // Nothing to assert on: it must simply do nothing and not throw, since explaining would reveal which addresses have accounts.
        new NoSignInDiagnostics().Explain(SignInHint.NoAccountForAddress, OtpChannel.Email, "candidate@example.com");
    }
}
