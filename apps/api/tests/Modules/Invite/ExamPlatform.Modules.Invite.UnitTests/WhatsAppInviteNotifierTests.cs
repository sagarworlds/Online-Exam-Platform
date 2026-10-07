using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Infrastructure.WhatsApp;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.Invite.UnitTests;

public class WhatsAppInviteNotifierTests
{
    private const string Code = "K7M2QX9A";
    private const string Link = "https://app.example/invite?code=K7M2QX9A";

    private static readonly DateTime Expires = new(2026, 10, 10, 14, 30, 0, DateTimeKind.Utc);

    private readonly IWhatsAppSender _whatsApp = Substitute.For<IWhatsAppSender>();
    private readonly ListLogger _logger = new();

    private sealed class ListLogger : ILogger<WhatsAppInviteNotifier>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private WhatsAppInviteNotifier Notifier(
        string? template = "exam_invitation", bool platformConfigured = true, string language = "en", string? country = "91", bool? enabled = true) =>
        new(
            _whatsApp,
            Options.Create(new InviteWhatsAppOptions { TemplateName = template, TemplateLanguage = language }),
            Options.Create(new WhatsAppOptions
            {
                Enabled = enabled,
                AccessToken = platformConfigured ? "token" : null,
                PhoneNumberId = platformConfigured ? "1234567890" : null,
                DefaultCountryCode = country,
            }),
            _logger);

    private static InviteWhatsAppMessage Message(string phone = "98765 43210", string exam = "Maths Final") =>
        new(phone, exam, Code, Link, Expires);

    private void Accepts() =>
        _whatsApp.SendTemplateAsync(Arg.Any<WhatsAppTemplateMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WhatsAppSendResult(true, "wamid.1"));

    [Theory]
    [InlineData("exam_invitation", true, true)]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("   ", true, false)]
    [InlineData("exam_invitation", false, false)]
    public void IsEnabled_NeedsATemplateAndAPlatformThatCanSend(string? template, bool platformConfigured, bool expected) =>
        Assert.Equal(expected, Notifier(template, platformConfigured).IsEnabled);

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task WithTheMasterSwitchOff_NothingIsSent_EvenWithATemplateAndCredentials(bool? switchedOn)
    {
        var notifier = Notifier(enabled: switchedOn);

        Assert.False(notifier.IsEnabled);
        Assert.False(await notifier.SendAsync(Message(), CancellationToken.None));
        await _whatsApp.DidNotReceiveWithAnyArgs().SendTemplateAsync(default!, default);
    }

    [Fact]
    public async Task SendAsync_WhenNotEnabled_SendsNothing()
    {
        var sent = await Notifier(template: null).SendAsync(Message(), CancellationToken.None);

        Assert.False(sent);
        await _whatsApp.DidNotReceiveWithAnyArgs().SendTemplateAsync(default!, default);
    }

    [Fact]
    public async Task SendAsync_SendsTheTemplateWithTheExamNameCodeLinkAndExpiry_InThatOrder()
    {
        Accepts();

        var sent = await Notifier().SendAsync(Message(), CancellationToken.None);

        Assert.True(sent);
        await _whatsApp.Received(1).SendTemplateAsync(
            Arg.Is<WhatsAppTemplateMessage>(m =>
                m.To == "919876543210"
                && m.TemplateName == "exam_invitation"
                && m.LanguageCode == "en"
                && m.BodyParameters.SequenceEqual(new[] { "Maths Final", Code, Link, "10 Oct 2026 14:30 UTC" })
                && m.UrlButtonParameter == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_UsesTheTemplateLanguageAndCountryCodeFromTheSettings()
    {
        Accepts();

        await Notifier(language: "hi", country: "44").SendAsync(Message("07911 123456"), CancellationToken.None);

        await _whatsApp.Received(1).SendTemplateAsync(
            Arg.Is<WhatsAppTemplateMessage>(m => m.To == "447911123456" && m.LanguageCode == "hi"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_PutsTheExamNameOnOneLine_BecauseWhatsAppRefusesLineBreaksInAValue()
    {
        Accepts();

        await Notifier().SendAsync(Message(exam: "  Maths\r\n\tFinal   2026  "), CancellationToken.None);

        await _whatsApp.Received(1).SendTemplateAsync(
            Arg.Is<WhatsAppTemplateMessage>(m => m.BodyParameters[0] == "Maths Final 2026"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_CutsAnExcessivelyLongExamName()
    {
        Accepts();

        await Notifier().SendAsync(Message(exam: new string('x', 500)), CancellationToken.None);

        await _whatsApp.Received(1).SendTemplateAsync(
            Arg.Is<WhatsAppTemplateMessage>(m => m.BodyParameters[0].Length == 200),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("12345")]
    public async Task SendAsync_ToSomethingThatIsNotAPhoneNumber_SendsNothing_AndLogsOnlyTheMaskedValue(string phone)
    {
        var sent = await Notifier().SendAsync(Message(phone), CancellationToken.None);

        Assert.False(sent);
        await _whatsApp.DidNotReceiveWithAnyArgs().SendTemplateAsync(default!, default);
        Assert.DoesNotContain(_logger.Messages, m => m.Contains(Code, StringComparison.Ordinal) || m.Contains(Link, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_WhenWhatsAppRefuses_ReportsNotSent()
    {
        _whatsApp.SendTemplateAsync(Arg.Any<WhatsAppTemplateMessage>(), Arg.Any<CancellationToken>()).Returns(new WhatsAppSendResult(false));

        Assert.False(await Notifier().SendAsync(Message(), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_NeverLogsTheCodeTheLinkOrTheWholeNumber()
    {
        Accepts();
        await Notifier().SendAsync(Message(), CancellationToken.None);
        _whatsApp.SendTemplateAsync(Arg.Any<WhatsAppTemplateMessage>(), Arg.Any<CancellationToken>()).Returns(new WhatsAppSendResult(false));
        await Notifier().SendAsync(Message(), CancellationToken.None);

        Assert.NotEmpty(_logger.Messages);
        Assert.All(_logger.Messages, m =>
        {
            Assert.DoesNotContain(Code, m, StringComparison.Ordinal);
            Assert.DoesNotContain(Link, m, StringComparison.Ordinal);
            Assert.DoesNotContain("919876543210", m, StringComparison.Ordinal);
            Assert.DoesNotContain("98765 43210", m, StringComparison.Ordinal);
        });
    }
}
