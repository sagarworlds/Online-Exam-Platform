using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Ports;
using NSubstitute;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>Who staff are shown as enrolled in an exam: each account once, by e-mail address.</summary>
public class ExamRosterReaderTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private readonly Guid _exam = Guid.NewGuid();
    private readonly IInviteRepository _repository = Substitute.For<IInviteRepository>();

    private static InviteAggregate Accepted(Guid examId, string email, Guid userId, DateTime acceptedAt)
    {
        var invite = new InviteAggregate(examId, null, email, Guid.NewGuid(), Now);
        var code = invite.GenerateCode(72, Now).Code;
        invite.Accept(code, userId, email, acceptedAt);
        return invite;
    }

    private Task<IReadOnlyList<Contracts.EnrolledCandidate>> RosterAsync(params InviteAggregate[] invites)
    {
        _repository.ListAcceptedForExamAsync(_exam, Arg.Any<CancellationToken>()).Returns(invites);
        return new ExamRosterReader(_repository).GetEnrolledCandidatesAsync(_exam, CancellationToken.None);
    }

    [Fact]
    public async Task EachAcceptedInvite_IsOneEnrolledCandidate_ListedByEmailWithoutRegardToCase()
    {
        var zed = Guid.NewGuid();
        var amy = Guid.NewGuid();

        var roster = await RosterAsync(Accepted(_exam, "zed@example.com", zed, Now), Accepted(_exam, "Amy@example.com", amy, Now));

        Assert.Equal(["Amy@example.com", "zed@example.com"], roster.Select(c => c.Email));
        Assert.Equal([amy, zed], roster.Select(c => c.UserId));
    }

    [Fact]
    public async Task ACandidateInvitedTwice_IsStillOneCandidate_WithTheAddressTheyFirstAcceptedAt()
    {
        var user = Guid.NewGuid();

        var roster = await RosterAsync(
            Accepted(_exam, "second@example.com", user, Now.AddHours(2)),
            Accepted(_exam, "first@example.com", user, Now.AddHours(1)));

        var only = Assert.Single(roster);
        Assert.Equal(user, only.UserId);
        Assert.Equal("first@example.com", only.Email);
    }

    [Fact]
    public async Task AnExamNobodyAccepted_HasNoCandidates() => Assert.Empty(await RosterAsync());
}
