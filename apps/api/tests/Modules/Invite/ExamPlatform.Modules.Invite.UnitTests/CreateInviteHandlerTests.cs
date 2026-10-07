using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

public class CreateInviteHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Email = "candidate@example.com";

    private readonly IInviteRepository _repository = Substitute.For<IInviteRepository>();
    private readonly IInviteUnitOfWork _unitOfWork = Substitute.For<IInviteUnitOfWork>();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IInviteNotifier _notifier = Substitute.For<IInviteNotifier>();
    private readonly IInviteWhatsAppNotifier _whatsApp = Substitute.For<IInviteWhatsAppNotifier>();
    private readonly IContactDirectory _contacts = Substitute.For<IContactDirectory>();
    private readonly IInviteLinkBuilder _links = Substitute.For<IInviteLinkBuilder>();
    private readonly Guid _examId = Guid.NewGuid();
    private readonly CreateInviteHandler _handler;

    public CreateInviteHandlerTests()
    {
        _catalog.FindAsync(_examId, Arg.Any<CancellationToken>()).Returns(new ExamSnapshot(
            _examId, "Maths Final", null, false, Now, Now.AddHours(3), null, null, 1, 0, 0, []));
        _links.Build(Arg.Any<string>()).Returns(call => "https://app.example/invite?code=" + call.Arg<string>());
        _handler = new CreateInviteHandler(
            _repository, _unitOfWork, _catalog, _notifier, _whatsApp, _contacts, _links, new FakeClock(Now), NullLogger<CreateInviteHandler>.Instance);
    }

    private CreateInviteCommand Command(string email = Email, Guid? examId = null) =>
        new(examId ?? _examId, null, email, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_StoresTheInviteWithOneCode_ThenEmailsTheLink()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(Email, result.Email);
        Assert.Equal(_examId, result.ExamId);
        Assert.Equal("Maths Final", result.ExamName);
        Assert.Equal(InviteStatus.Pending, result.Status);
        Assert.True(result.EmailSent);

        // Saved before it is sent, so a mail failure can never lose the invite.
        Received.InOrder(() =>
        {
            _repository.Add(Arg.Is<InviteAggregate>(i => i.Email == Email && i.Codes.Count == 1));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _notifier.SendAsync(
                Arg.Is<InviteEmail>(m => m.To == Email && m.ExamName == "Maths Final" && m.Link.StartsWith("https://app.example/invite?code=")),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_WhenTheEmailWasSent_DoesNotHandTheLinkBackToTheInviter()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.Null(result.InviteLink);
        Assert.Null(result.InviteCode);
    }

    [Fact]
    public async Task HandleAsync_WhenNoEmailWasSent_GivesTheInviterTheLinkToPassOn()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.EmailSent);
        Assert.NotNull(result.InviteLink);
        Assert.StartsWith("https://app.example/invite?code=", result.InviteLink);

        // The code the link carries is handed back with it, so the inviter can copy either one.
        Assert.Matches("^[A-Z0-9]{8}$", result.InviteCode);
        Assert.EndsWith(result.InviteCode!, result.InviteLink);
        // The invite itself is kept: only the delivery failed.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TheEmailedLinkCarriesTheCodeOfTheStoredInvite()
    {
        InviteAggregate? stored = null;
        _repository.When(r => r.Add(Arg.Any<InviteAggregate>())).Do(call => stored = call.Arg<InviteAggregate>());
        InviteEmail? sent = null;
        _notifier.SendAsync(Arg.Do<InviteEmail>(m => sent = m), Arg.Any<CancellationToken>()).Returns(true);

        await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.NotNull(stored);
        Assert.EndsWith(stored!.Codes.Single().Code, sent!.Link);
        Assert.Equal(stored.Codes.Single().ExpiresAt, sent.ExpiresAtUtc);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task HandleAsync_WithAnInvalidEmail_Throws400AndStoresAndSendsNothing(string email)
    {
        var error = await Assert.ThrowsAsync<InvalidInviteEmailError>(() => _handler.HandleAsync(Command(email), CancellationToken.None));

        Assert.Equal(400, error.HttpStatusCode);
        _repository.DidNotReceive().Add(Arg.Any<InviteAggregate>());
        await _notifier.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_ForAnUnknownExam_Throws404AndStoresAndSendsNothing()
    {
        var error = await Assert.ThrowsAsync<InviteExamNotFoundError>(
            () => _handler.HandleAsync(Command(examId: Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(404, error.HttpStatusCode);
        _repository.DidNotReceive().Add(Arg.Any<InviteAggregate>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    // ---- the exam code on WhatsApp --------------------------------------------------------------------------------------

    // The host sends invitations on WhatsApp, and the invited address belongs to an account with this phone number.
    private void WhatsAppOn(string? registeredPhone, bool accepted = true)
    {
        _whatsApp.IsEnabled.Returns(true);
        _contacts.FindPhoneNumberByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(registeredPhone);
        _whatsApp.SendAsync(Arg.Any<InviteWhatsAppMessage>(), Arg.Any<CancellationToken>()).Returns(accepted);
    }

    [Fact]
    public async Task HandleAsync_WhenTheInvitedPersonHasARegisteredPhone_AlsoSendsThemTheExamCodeOnWhatsApp()
    {
        InviteAggregate? stored = null;
        _repository.When(r => r.Add(Arg.Any<InviteAggregate>())).Do(call => stored = call.Arg<InviteAggregate>());
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);
        WhatsAppOn("98765 43210");

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.EmailSent);
        Assert.True(result.WhatsAppSent);
        var code = stored!.Codes.Single();
        await _whatsApp.Received(1).SendAsync(
            Arg.Is<InviteWhatsAppMessage>(m =>
                m.PhoneNumber == "98765 43210"
                && m.ExamName == "Maths Final"
                && m.Code == code.Code
                && m.Link == "https://app.example/invite?code=" + code.Code
                && m.ExpiresAtUtc == code.ExpiresAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SendsOnWhatsAppOnlyAfterTheInviteIsSaved()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);
        WhatsAppOn("9876543210");

        await _handler.HandleAsync(Command(), CancellationToken.None);

        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _whatsApp.SendAsync(Arg.Any<InviteWhatsAppMessage>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_WhenTheHostDoesNotSendOnWhatsApp_NeverLooksAPhoneNumberUp()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.WhatsAppSent);
        // Nobody's number is read for a message that will not be sent.
        await _contacts.DidNotReceiveWithAnyArgs().FindPhoneNumberByEmailAsync(default!, default);
        await _whatsApp.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenTheInvitedAddressHasNoRegisteredPhone_SendsNothingOnWhatsApp()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);
        WhatsAppOn(registeredPhone: null);

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.EmailSent);
        Assert.False(result.WhatsAppSent);
        await _whatsApp.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenWhatsAppDoesNotTakeTheMessage_StillReturnsTheInviteAndItsEmail()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);
        WhatsAppOn("9876543210", accepted: false);

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.EmailSent);
        Assert.False(result.WhatsAppSent);
        Assert.Null(result.InviteLink);
    }

    [Fact]
    public async Task HandleAsync_WhenOnlyWhatsAppDelivered_DoesNotHandTheLinkBackToTheInviter()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(false);
        WhatsAppOn("9876543210");

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.EmailSent);
        Assert.True(result.WhatsAppSent);
        // The message carried the code and the link, so there is nothing for the inviter to pass on.
        Assert.Null(result.InviteLink);
        Assert.Null(result.InviteCode);
    }

    [Fact]
    public async Task HandleAsync_WhenNeitherTheEmailNorWhatsAppWasSent_GivesTheInviterTheLink()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(false);
        WhatsAppOn("9876543210", accepted: false);

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.EmailSent);
        Assert.False(result.WhatsAppSent);
        Assert.StartsWith("https://app.example/invite?code=", result.InviteLink);
        Assert.EndsWith(result.InviteCode!, result.InviteLink);
    }

    [Fact]
    public async Task HandleAsync_WhenTheLookupFails_KeepsTheInviteAndTheEmail()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);
        _whatsApp.IsEnabled.Returns(true);
        _contacts.FindPhoneNumberByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        // The invite was saved and mailed before WhatsApp was tried; a failure there must not turn that into an error.
        Assert.True(result.EmailSent);
        Assert.False(result.WhatsAppSent);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTheCallerCancelsDuringWhatsApp_StopsInsteadOfSwallowingIt()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(true);
        _whatsApp.IsEnabled.Returns(true);
        _contacts.FindPhoneNumberByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _handler.HandleAsync(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithAnInvalidEmail_NeverLooksAPhoneNumberUp()
    {
        WhatsAppOn("9876543210");

        await Assert.ThrowsAsync<InvalidInviteEmailError>(() => _handler.HandleAsync(Command("not-an-email"), CancellationToken.None));

        await _contacts.DidNotReceiveWithAnyArgs().FindPhoneNumberByEmailAsync(default!, default);
    }
}
