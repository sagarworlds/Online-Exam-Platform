using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
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
    private readonly IInviteLinkBuilder _links = Substitute.For<IInviteLinkBuilder>();
    private readonly Guid _examId = Guid.NewGuid();
    private readonly CreateInviteHandler _handler;

    public CreateInviteHandlerTests()
    {
        _catalog.FindAsync(_examId, Arg.Any<CancellationToken>()).Returns(new ExamSnapshot(
            _examId, "Maths Final", null, false, Now, Now.AddHours(3), null, null, 1, 0, 0, []));
        _links.Build(Arg.Any<string>()).Returns(call => "https://app.example/invite?code=" + call.Arg<string>());
        _handler = new CreateInviteHandler(
            _repository, _unitOfWork, _catalog, _notifier, _links, new FakeClock(Now), NullLogger<CreateInviteHandler>.Instance);
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
    }

    [Fact]
    public async Task HandleAsync_WhenNoEmailWasSent_GivesTheInviterTheLinkToPassOn()
    {
        _notifier.SendAsync(Arg.Any<InviteEmail>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.EmailSent);
        Assert.NotNull(result.InviteLink);
        Assert.StartsWith("https://app.example/invite?code=", result.InviteLink);
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
}
