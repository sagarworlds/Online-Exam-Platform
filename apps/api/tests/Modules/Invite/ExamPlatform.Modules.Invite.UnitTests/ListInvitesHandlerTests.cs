using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Application.Queries;
using NSubstitute;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>The invite listing staff read: every invite carries the name of its exam, so the screen can group and label them.</summary>
public class ListInvitesHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IInviteRepository _repository = Substitute.For<IInviteRepository>();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly ListInvitesHandler _handler;

    public ListInvitesHandlerTests() => _handler = new ListInvitesHandler(_repository, _catalog);

    private void KnownExam(Guid examId, string name) =>
        _catalog.FindAsync(examId, Arg.Any<CancellationToken>()).Returns(new ExamSnapshot(
            examId, name, null, false, Now, Now.AddHours(3), null, null, 1, 0, 0, []));

    private static InviteAggregate InviteTo(Guid examId, string email) => new(examId, null, email, Guid.NewGuid(), Now);

    [Fact]
    public async Task EveryInvite_CarriesTheNameOfItsExam()
    {
        var maths = Guid.NewGuid();
        var physics = Guid.NewGuid();
        KnownExam(maths, "Maths Final");
        KnownExam(physics, "Physics Quiz");
        _repository.ListNewestAsync(ListInvitesHandler.PageSize, Arg.Any<CancellationToken>())
            .Returns([InviteTo(maths, "a@example.com"), InviteTo(physics, "a@example.com"), InviteTo(maths, "b@example.com")]);

        var listed = await _handler.HandleAsync(CancellationToken.None);

        Assert.Equal(["Maths Final", "Physics Quiz", "Maths Final"], listed.Select(i => i.ExamName));
        Assert.Equal(["a@example.com", "a@example.com", "b@example.com"], listed.Select(i => i.Email));
    }

    [Fact]
    public async Task AnExamWithManyInvites_IsLookedUpOnce()
    {
        var exam = Guid.NewGuid();
        KnownExam(exam, "Maths Final");
        _repository.ListNewestAsync(ListInvitesHandler.PageSize, Arg.Any<CancellationToken>())
            .Returns([InviteTo(exam, "a@example.com"), InviteTo(exam, "b@example.com"), InviteTo(exam, "c@example.com")]);

        await _handler.HandleAsync(CancellationToken.None);

        await _catalog.Received(1).FindAsync(exam, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnInviteWhoseExamCannotBeFound_StaysInTheList_WithNoName()
    {
        var known = Guid.NewGuid();
        var gone = Guid.NewGuid();
        KnownExam(known, "Maths Final");
        _catalog.FindAsync(gone, Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);
        _repository.ListNewestAsync(ListInvitesHandler.PageSize, Arg.Any<CancellationToken>())
            .Returns([InviteTo(gone, "a@example.com"), InviteTo(known, "a@example.com")]);

        var listed = await _handler.HandleAsync(CancellationToken.None);

        Assert.Equal(2, listed.Count);
        Assert.Null(listed[0].ExamName);
        Assert.Equal("Maths Final", listed[1].ExamName);
    }

    [Fact]
    public async Task NoInvites_AreAnEmptyList_AndNoExamIsLookedUp()
    {
        _repository.ListNewestAsync(ListInvitesHandler.PageSize, Arg.Any<CancellationToken>()).Returns([]);

        var listed = await _handler.HandleAsync(CancellationToken.None);

        Assert.Empty(listed);
        await _catalog.DidNotReceiveWithAnyArgs().FindAsync(default, default);
    }
}
