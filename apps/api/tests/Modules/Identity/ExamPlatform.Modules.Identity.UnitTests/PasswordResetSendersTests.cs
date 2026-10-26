using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>What the password-reset message says and where it goes: a link, the code for pasting, and the lifetime the code really has.</summary>
public class PasswordResetSendersTests
{
    private const string Email = "amy@example.com";
    private const string Code = "3f2a9c1e-0b7d-4c55-9a60-1d2e3f4a5b6c:ABCDEF0123456789";
    private const string Page = "https://exam.example/password-reset/confirm";

    private readonly IMailSender _mail = Substitute.For<IMailSender>();
    private readonly IPasswordResetLinkBuilder _links = Substitute.For<IPasswordResetLinkBuilder>();
    private readonly ILogger<SmtpPasswordResetSender> _smtpLogger = Substitute.For<ILogger<SmtpPasswordResetSender>>();
    private readonly ILogger<LoggingPasswordResetSender> _devLogger = Substitute.For<ILogger<LoggingPasswordResetSender>>();

    public PasswordResetSendersTests()
    {
        _links.Build(Code).Returns($"{Page}#code=3f2a9c1e-0b7d-4c55-9a60-1d2e3f4a5b6c%3AABCDEF0123456789");
        _mail.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private async Task<OutgoingMail> MailSentAsync(TimeSpan validity)
    {
        OutgoingMail? sent = null;
        await _mail.SendAsync(Arg.Do<OutgoingMail>(mail => sent = mail), Arg.Any<CancellationToken>());

        await new SmtpPasswordResetSender(_mail, _links, _smtpLogger).SendAsync(Email, Code, validity, CancellationToken.None);

        return Assert.IsType<OutgoingMail>(sent);
    }

    [Fact]
    public async Task Smtp_MailsALinkThatOpensTheResetPageWithTheCodeFilledIn()
    {
        var mail = await MailSentAsync(TimeSpan.FromMinutes(30));

        Assert.Equal(Email, mail.To);
        Assert.Contains(_links.Build(Code), mail.Body);
        Assert.Equal(SmtpPasswordResetSender.Subject, mail.Subject);
    }

    [Fact]
    public async Task Smtp_AlsoShowsTheCodeAndThePageItGoesOn_ForAMailProgramThatDoesNotOpenLinks()
    {
        var mail = await MailSentAsync(TimeSpan.FromMinutes(30));

        Assert.Contains($"go to {Page} and paste this code", mail.Body);
        Assert.Contains(Code, mail.Body);
    }

    [Fact]
    public async Task Smtp_StatesTheLifetimeItWasGiven_NotTheTenMinutesOfASignInCode()
    {
        var mail = await MailSentAsync(TimeSpan.FromMinutes(30));

        Assert.Contains("expires in 30 minutes", mail.Body);
        Assert.DoesNotContain("10 minutes", mail.Body);
    }

    [Theory]
    [InlineData(1, "expires in 1 minute.")]
    [InlineData(90, "expires in 90 minutes.")]
    [InlineData(0.5, "expires in 1 minute.")]
    public async Task Smtp_RoundsTheLifetimeUp_SoItNeverPromisesMoreTimeThanThereIs(double minutes, string expected)
    {
        var mail = await MailSentAsync(TimeSpan.FromMinutes(minutes));

        Assert.Contains(expected, mail.Body);
    }

    [Fact]
    public async Task Smtp_WhenTheMailServerRefuses_LogsAnErrorWithoutTheCode_AndDoesNotThrow()
    {
        _mail.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(false);

        // The request is answered the same for every address, so a failure must be logged, not surfaced as an error.
        await new SmtpPasswordResetSender(_mail, _links, _smtpLogger).SendAsync(Email, Code, TimeSpan.FromMinutes(30), CancellationToken.None);

        var entry = Assert.Single(LogEntries(_smtpLogger), e => e.Level == LogLevel.Error);
        Assert.Contains("a***@example.com", entry.Message);
        Assert.DoesNotContain(Email, entry.Message);
        Assert.DoesNotContain(Code, entry.Message);
    }

    [Fact]
    public async Task DevelopmentLog_LogsTheLinkForAMaskedDestination_NeverTheRawAddress()
    {
        await new LoggingPasswordResetSender(_links, _devLogger).SendAsync(Email, Code, TimeSpan.FromMinutes(30), CancellationToken.None);

        var entry = Assert.Single(LogEntries(_devLogger));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("a***@example.com", entry.Message);
        Assert.DoesNotContain(Email, entry.Message);

        // Still logged, because resetting a password during local development depends on reading it here.
        Assert.Contains(_links.Build(Code), entry.Message);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    // ILogger.Log is generic, so a substitute records it as a call whose arguments are the level, the state and the formatter.
    private static List<LogEntry> LogEntries<T>(ILogger<T> logger) => logger.ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
        .Select(call =>
        {
            var args = call.GetArguments();
            var message = ((Delegate)args[4]!).DynamicInvoke(args[2], args[3]) as string ?? string.Empty;
            return new LogEntry((LogLevel)args[0]!, message);
        })
        .ToList();
}
