using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>A candidate's dispute of an answer key (FR-31): raised once, then settled once, by a correction or a rejection.</summary>
public class DisputeTests
{
    private readonly Guid _attempt = Guid.NewGuid();
    private readonly Guid _exam = Guid.NewGuid();
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _question = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();

    private Dispute Open(string? reason = "Option B is also correct") =>
        Dispute.Raise(_attempt, _exam, _candidate, _question, reason, Fixtures.Now);

    [Fact]
    public void ARaisedDispute_IsOpen_KeepsWhoWhatAndWhen_AndRaisesAnEventWithoutTheReason()
    {
        var dispute = Open("  Option B is also correct  ");

        Assert.Equal(DisputeStatus.Open, dispute.Status);
        Assert.Equal(_attempt, dispute.AttemptId);
        Assert.Equal(_exam, dispute.ExamId);
        Assert.Equal(_candidate, dispute.CandidateId);
        Assert.Equal(_question, dispute.QuestionId);
        Assert.Equal("Option B is also correct", dispute.Reason);
        Assert.Equal(Fixtures.Now, dispute.RaisedAtUtc);
        Assert.Null(dispute.ResolvedAtUtc);
        Assert.Null(dispute.ResolutionNote);

        var raised = Assert.IsType<DisputeRaisedEvent>(Assert.Single(dispute.DomainEvents));
        Assert.Equal(dispute.Id, raised.DisputeId);
        Assert.Equal(_question, raised.QuestionId);
        // The event is what the audit trail is written from; the candidate's words are not in it.
        Assert.DoesNotContain(nameof(Dispute.Reason), typeof(DisputeRaisedEvent).GetProperties().Select(p => p.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ADisputeWithoutAReason_IsRefused(string? reason)
    {
        Assert.Throws<InvalidAttemptError>(() => Open(reason));
    }

    [Fact]
    public void AReasonOfTheLongestLength_IsAccepted_AndOneCharacterMoreIsNot()
    {
        Assert.Equal(Dispute.MaxReasonLength, Open(new string('a', Dispute.MaxReasonLength)).Reason.Length);
        Assert.Throws<InvalidAttemptError>(() => Open(new string('a', Dispute.MaxReasonLength + 1)));
    }

    [Fact]
    public void Accepting_RecordsWhoWhenAndWhy_AndRaisesAnEvent()
    {
        var dispute = Open();
        dispute.ClearDomainEvents();

        dispute.Accept(_staff, Fixtures.Now.AddHours(2), "The key was wrong");

        Assert.Equal(DisputeStatus.Accepted, dispute.Status);
        Assert.Equal(_staff, dispute.ResolvedByUserId);
        Assert.Equal(Fixtures.Now.AddHours(2), dispute.ResolvedAtUtc);
        Assert.Equal("The key was wrong", dispute.ResolutionNote);
        var resolved = Assert.IsType<DisputeResolvedEvent>(Assert.Single(dispute.DomainEvents));
        Assert.True(resolved.Accepted);
    }

    [Fact]
    public void Accepting_WithNoStaffUserKnown_IsStillRecorded()
    {
        var dispute = Open();

        dispute.Accept(null, Fixtures.Now, "Corrected");

        Assert.Equal(DisputeStatus.Accepted, dispute.Status);
        Assert.Null(dispute.ResolvedByUserId);
    }

    [Fact]
    public void Rejecting_RecordsWhoWhenAndWhy_AndRaisesAnEvent()
    {
        var dispute = Open();
        dispute.ClearDomainEvents();

        dispute.Reject(_staff, Fixtures.Now.AddHours(2), "  The key is right: 4 is the only sum.  ");

        Assert.Equal(DisputeStatus.Rejected, dispute.Status);
        Assert.Equal(_staff, dispute.ResolvedByUserId);
        Assert.Equal("The key is right: 4 is the only sum.", dispute.ResolutionNote);
        var resolved = Assert.IsType<DisputeResolvedEvent>(Assert.Single(dispute.DomainEvents));
        Assert.False(resolved.Accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejecting_WithoutAnExplanation_IsRefused_AndLeavesTheDisputeOpen(string? note)
    {
        var dispute = Open();

        Assert.Throws<InvalidAttemptError>(() => dispute.Reject(_staff, Fixtures.Now, note));

        Assert.Equal(DisputeStatus.Open, dispute.Status);
    }

    [Fact]
    public void ANoteOfTheLongestLength_IsAccepted_AndOneCharacterMoreIsNot()
    {
        Open().Reject(_staff, Fixtures.Now, new string('a', Dispute.MaxNoteLength));

        var dispute = Open();
        Assert.Throws<InvalidAttemptError>(() => dispute.Reject(_staff, Fixtures.Now, new string('a', Dispute.MaxNoteLength + 1)));
        Assert.Equal(DisputeStatus.Open, dispute.Status);
    }

    [Fact]
    public void ASettledDispute_CannotBeSettledAgain_EitherWay()
    {
        var accepted = Open();
        accepted.Accept(_staff, Fixtures.Now, "Corrected");
        var rejected = Open();
        rejected.Reject(_staff, Fixtures.Now, "The key stands");

        Assert.Throws<DisputeNotOpenError>(() => accepted.Reject(_staff, Fixtures.Now, "Changed my mind"));
        Assert.Throws<DisputeNotOpenError>(() => accepted.Accept(_staff, Fixtures.Now, "Again"));
        Assert.Throws<DisputeNotOpenError>(() => rejected.Accept(_staff, Fixtures.Now, "Corrected"));
        Assert.Throws<DisputeNotOpenError>(() => rejected.Reject(_staff, Fixtures.Now, "Again"));
        Assert.Equal("Corrected", accepted.ResolutionNote);
        Assert.Equal("The key stands", rejected.ResolutionNote);
    }

    [Fact]
    public void ASecondRejection_IsRefusedAsAlreadySettled_NotAsAMissingNote()
    {
        var dispute = Open();
        dispute.Reject(_staff, Fixtures.Now, "The key stands");

        Assert.Throws<DisputeNotOpenError>(() => dispute.Reject(_staff, Fixtures.Now, null));
    }
}
