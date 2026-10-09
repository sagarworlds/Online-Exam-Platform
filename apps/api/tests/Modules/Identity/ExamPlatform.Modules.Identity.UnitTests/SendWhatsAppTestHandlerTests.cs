using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>An administrator's WhatsApp test: what is accepted, what is sent, and what is audited.</summary>
public class SendWhatsAppTestHandlerTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly IWhatsAppDiagnostics _diagnostics = Substitute.For<IWhatsAppDiagnostics>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly SendWhatsAppTestHandler _handler;

    public SendWhatsAppTestHandlerTests()
    {
        _handler = new SendWhatsAppTestHandler(_diagnostics, _audit);
    }

    private static SendWhatsAppTestCommand Command(string? number = "9876543210", string? mode = "Text", string? message = "Hello") =>
        new(number, mode, message, Admin, "SuperAdmin");

    private static WhatsAppSendResultDto Accepted(string mode = "Text") =>
        new(true, "wamid.1", "********10", mode, true, null, null);

    private static WhatsAppSendResultDto Refused(string kind) =>
        new(false, null, "********10", "Text", false, null, new WhatsAppFailureDto(kind, "Why.", 131047, "Re-engagement message", 400));

    [Fact]
    public async Task ATextMessage_IsSentTrimmed_AndTheResultReturned()
    {
        _diagnostics.SendAsync(WhatsAppTestMode.Text, "9876543210", "Hello there", Arg.Any<CancellationToken>()).Returns(Accepted());

        var result = await _handler.HandleAsync(Command(number: "  9876543210 ", message: "  Hello there  "), CancellationToken.None);

        Assert.True(result.Sent);
        Assert.Equal("wamid.1", result.MessageId);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("TEXT")]
    [InlineData(" Text ")]
    public async Task TheModeIsReadIgnoringCase(string mode)
    {
        _diagnostics.SendAsync(WhatsAppTestMode.Text, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Accepted());

        await _handler.HandleAsync(Command(mode: mode), CancellationToken.None);

        await _diagnostics.Received(1).SendAsync(WhatsAppTestMode.Text, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheSignInTemplate_NeedsNoMessage_AndNoneIsPassedOn()
    {
        _diagnostics.SendAsync(WhatsAppTestMode.SignInTemplate, "9876543210", null, Arg.Any<CancellationToken>()).Returns(Accepted("SignInTemplate"));

        // A message typed before switching to the template is not sent along with it.
        var result = await _handler.HandleAsync(Command(mode: "SignInTemplate", message: "left over"), CancellationToken.None);

        Assert.Equal("SignInTemplate", result.Mode);
        await _diagnostics.Received(1).SendAsync(WhatsAppTestMode.SignInTemplate, "9876543210", null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithoutANumber_NothingIsSent(string? number)
    {
        var error = await Assert.ThrowsAsync<InvalidWhatsAppMessageError>(() => _handler.HandleAsync(Command(number: number), CancellationToken.None));

        Assert.Equal("invalid_whatsapp_message", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
        await _diagnostics.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default, default);
    }

    [Fact]
    public async Task ANumberLongerThanAnyRealOne_IsRefusedBeforeItReachesMeta()
    {
        await Assert.ThrowsAsync<InvalidWhatsAppMessageError>(() => _handler.HandleAsync(Command(number: new string('9', 33)), CancellationToken.None));

        await _diagnostics.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Carrier-Pigeon")]
    [InlineData("7")]
    public async Task AnUnknownMode_IsRefused(string? mode)
    {
        var error = await Assert.ThrowsAsync<InvalidWhatsAppMessageError>(() => _handler.HandleAsync(Command(mode: mode), CancellationToken.None));

        Assert.Contains("Text", error.Message, StringComparison.Ordinal);
        await _diagnostics.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ATextMessageWithoutWords_IsRefused(string? message)
    {
        await Assert.ThrowsAsync<InvalidWhatsAppMessageError>(() => _handler.HandleAsync(Command(message: message), CancellationToken.None));

        await _diagnostics.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default, default);
    }

    [Fact]
    public async Task AMessageOfTheLongestLength_IsSent_AndOneCharacterMoreIsRefused()
    {
        _diagnostics.SendAsync(Arg.Any<WhatsAppTestMode>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Accepted());

        await _handler.HandleAsync(Command(message: new string('a', SendWhatsAppTestHandler.MaxMessageLength)), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidWhatsAppMessageError>(
            () => _handler.HandleAsync(Command(message: new string('a', SendWhatsAppTestHandler.MaxMessageLength + 1)), CancellationToken.None));

        await _diagnostics.Received(1).SendAsync(Arg.Any<WhatsAppTestMode>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EverySend_IsAudited_WithWhoHowAndTheOutcome_ButNotTheWords()
    {
        _diagnostics.SendAsync(Arg.Any<WhatsAppTestMode>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Accepted());

        await _handler.HandleAsync(Command(message: "Meet me at the station, Priya"), CancellationToken.None);

        var entry = Assert.Single(_audit.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<AuditEntry>());
        Assert.Equal("Identity.WhatsAppTestSent", entry.Action);
        Assert.Equal(Admin, entry.ActorUserId);
        Assert.Equal("SuperAdmin", entry.ActorRole);
        Assert.Equal("wamid.1", entry.EntityId);
        Assert.Equal("Text", entry.Metadata!["mode"]);
        Assert.Equal("Sent", entry.Metadata["outcome"]);
        Assert.Equal("********10", entry.Metadata["to"]);
        Assert.Equal("wamid.1", entry.Metadata["messageId"]);
        Assert.Equal("29", entry.Metadata["length"]);
        // Neither the number nor the words are recorded.
        var recorded = string.Join('|', entry.Metadata.Values) + entry.EntityId;
        Assert.DoesNotContain("9876543210", recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("station", recorded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedSend_IsAuditedWithTheKindOfFailure_AndReturnedForTheAdministratorToSee()
    {
        _diagnostics.SendAsync(Arg.Any<WhatsAppTestMode>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Refused("ReEngagementRequired"));

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.Sent);
        Assert.Equal("ReEngagementRequired", result.Failure!.Kind);
        var entry = Assert.Single(_audit.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<AuditEntry>());
        Assert.Equal("ReEngagementRequired", entry.Metadata!["outcome"]);
        Assert.Equal("test", entry.EntityId);
        Assert.False(entry.Metadata.ContainsKey("messageId"));
    }

    [Fact]
    public async Task ATemplateSend_RecordsNoLength()
    {
        _diagnostics.SendAsync(WhatsAppTestMode.SignInTemplate, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Accepted("SignInTemplate"));

        await _handler.HandleAsync(Command(mode: "SignInTemplate"), CancellationToken.None);

        var entry = Assert.Single(_audit.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<AuditEntry>());
        Assert.Equal("SignInTemplate", entry.Metadata!["mode"]);
        Assert.False(entry.Metadata.ContainsKey("length"));
    }

    [Fact]
    public async Task ARequestThatIsRefusedAsMalformed_IsNotAudited_BecauseNothingWasSent()
    {
        await Assert.ThrowsAsync<InvalidWhatsAppMessageError>(() => _handler.HandleAsync(Command(number: ""), CancellationToken.None));

        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }
}
