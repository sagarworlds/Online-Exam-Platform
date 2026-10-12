using ExamPlatform.Modules.Batch.Domain;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

/// <summary>
/// The status changes one seat goes through as its member is invited and registers. The aggregate tests cover
/// adding and removing members; these cover the member's own state.
/// </summary>
public class BatchMemberTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(2);

    private static BatchMember NewMember()
    {
        var batch = new BatchAggregate(Guid.NewGuid(), "Batch A", null, 10, Guid.NewGuid(), Now);
        batch.AddMember("amy@example.com", null, Now);
        return Assert.Single(batch.Members);
    }

    [Fact]
    public void NewMember_IsInvitedAndHasNoCandidateYet()
    {
        var member = NewMember();

        Assert.Equal(MemberRegistrationStatus.Invited, member.Status);
        Assert.Null(member.CandidateId);
        Assert.Null(member.InviteSentAt);
        Assert.Null(member.RegistrationCompletedAt);
    }

    [Fact]
    public void MarkInviteSent_RecordsTheInstant_AndLeavesTheStatusAlone()
    {
        var member = NewMember();

        member.MarkInviteSent(Later);

        Assert.Equal(Later, member.InviteSentAt);
        Assert.Equal(Later, member.UpdatedAt);
        Assert.Equal(MemberRegistrationStatus.Invited, member.Status);
    }

    [Fact]
    public void MarkRegistrationCompleted_LinksTheCandidate_AndMarksTheMemberRegistered()
    {
        var member = NewMember();
        var candidateId = Guid.NewGuid();

        member.MarkRegistrationCompleted(candidateId, Later);

        Assert.Equal(candidateId, member.CandidateId);
        Assert.Equal(MemberRegistrationStatus.Registered, member.Status);
        Assert.Equal(Later, member.RegistrationCompletedAt);
        Assert.Equal(Later, member.UpdatedAt);
    }

    [Fact]
    public void Withdraw_MarksTheMemberWithdrawn_AndKeepsTheRecord()
    {
        var member = NewMember();

        member.Withdraw(Later);

        Assert.Equal(MemberRegistrationStatus.Withdrawn, member.Status);
        Assert.False(member.IsDeleted);
        Assert.Equal(Later, member.UpdatedAt);
    }
}
