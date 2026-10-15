using ExamPlatform.Modules.Batch.Domain;
using ExamPlatform.Modules.Batch.Domain.Events;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

/// <summary>
/// Importing a roster into a batch (FR-50). The batch judges every row under the rules a single add follows, a bad row
/// never stops the rows after it, and the whole file is audited as one event.
/// </summary>
public class BatchRosterImportTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(1);

    private static BatchAggregate NewBatch(int maxMembers = 10) =>
        new(Guid.NewGuid(), "Batch A", null, maxMembers, Guid.NewGuid(), Now);

    private static RosterRow Row(int row, string? email, string? phone = null) => new(row, email, phone);

    private static IEnumerable<RosterRowOutcome> Outcomes(IReadOnlyList<RosterRowResult> results) => results.Select(r => r.Outcome);

    [Fact]
    public void ImportMembers_SeatsEveryGoodRow_InFileOrder_AndReturnsTheNewMemberIds()
    {
        var batch = NewBatch();

        var results = batch.ImportMembers([Row(2, "amy@example.com"), Row(3, "ben@example.com", "+91 98765 43210")], Later);

        Assert.Equal([RosterRowOutcome.Added, RosterRowOutcome.Added], Outcomes(results));
        Assert.Equal([2, 3], results.Select(r => r.Row));
        Assert.Equal(batch.Members.Select(m => m.Id), results.Select(r => r.MemberId!.Value));
        Assert.Equal(["amy@example.com", "ben@example.com"], batch.Members.Select(m => m.Email));
        Assert.Equal(Later, batch.UpdatedAt);
    }

    [Fact]
    public void ImportMembers_StoresTheAddressAndPhoneAsAnAddWould()
    {
        var batch = NewBatch();

        batch.ImportMembers([Row(1, "  Jane.Doe@Example.COM ", "+91-98765 43210")], Now);

        var member = Assert.Single(batch.Members);
        Assert.Equal("jane.doe@example.com", member.Email);
        Assert.Equal("+919876543210", member.Phone);
    }

    [Theory]
    [InlineData(null, RosterRowOutcome.MissingEmail)]
    [InlineData("", RosterRowOutcome.MissingEmail)]
    [InlineData("   ", RosterRowOutcome.MissingEmail)]
    [InlineData("not-an-address", RosterRowOutcome.InvalidEmail)]
    [InlineData("Amy <amy@example.com>", RosterRowOutcome.InvalidEmail)]
    public void ImportMembers_WithAnUnusableAddress_SkipsTheRowAndSaysWhy(string? email, RosterRowOutcome expected)
    {
        var batch = NewBatch();

        var results = batch.ImportMembers([Row(2, email)], Now);

        var result = Assert.Single(results);
        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.MemberId);
        Assert.Empty(batch.Members);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("+0123456789")]
    [InlineData("98765abc")]
    public void ImportMembers_WithAnUnusablePhone_SkipsTheRow_RatherThanSeatingItWithoutThePhone(string phone)
    {
        // Seating the candidate and dropping the number would hide the mistake in the file, so the row is
        // reported and the person can fix it or leave the phone blank.
        var batch = NewBatch();

        var results = batch.ImportMembers([Row(2, "amy@example.com", phone)], Now);

        Assert.Equal(RosterRowOutcome.InvalidPhone, Assert.Single(results).Outcome);
        Assert.Empty(batch.Members);
    }

    [Fact]
    public void ImportMembers_WithABlankPhone_SeatsTheRow()
    {
        var batch = NewBatch();

        var results = batch.ImportMembers([Row(2, "amy@example.com", "   ")], Now);

        Assert.Equal(RosterRowOutcome.Added, Assert.Single(results).Outcome);
        Assert.Null(Assert.Single(batch.Members).Phone);
    }

    [Fact]
    public void ImportMembers_ABadRow_DoesNotStopTheRowsAroundIt()
    {
        var batch = NewBatch();

        var results = batch.ImportMembers([Row(2, "amy@example.com"), Row(3, "nope"), Row(4, "ben@example.com")], Now);

        Assert.Equal([RosterRowOutcome.Added, RosterRowOutcome.InvalidEmail, RosterRowOutcome.Added], Outcomes(results));
        Assert.Equal(2, batch.GetActiveMemberCount());
    }

    [Fact]
    public void ImportMembers_AnAddressAlreadyInTheBatch_IsSkipped_InAnyCase()
    {
        var batch = NewBatch();
        batch.AddMember("amy@example.com", null, Now);

        var results = batch.ImportMembers([Row(2, "  AMY@Example.com ")], Later);

        Assert.Equal(RosterRowOutcome.AlreadyInBatch, Assert.Single(results).Outcome);
        Assert.Equal(1, batch.GetActiveMemberCount());
    }

    [Fact]
    public void ImportMembers_AnAddressRepeatedInTheFile_IsSkipped_AndNamesTheRowThatTookTheSeat()
    {
        var batch = NewBatch();

        var results = batch.ImportMembers([Row(2, "amy@example.com"), Row(3, "ben@example.com"), Row(4, "AMY@example.com")], Now);

        Assert.Equal([RosterRowOutcome.Added, RosterRowOutcome.Added, RosterRowOutcome.RepeatedInFile], Outcomes(results));
        Assert.Equal(2, results[2].FirstRow);
        Assert.Equal(2, batch.GetActiveMemberCount());
    }

    [Fact]
    public void ImportMembers_ARowTurnedDownForItsPhone_DoesNotUseUpItsAddress()
    {
        // The first row never got a seat, so the second row for the same person is not a repeat; it can.
        var batch = NewBatch();

        var results = batch.ImportMembers([Row(2, "amy@example.com", "12"), Row(3, "amy@example.com", "+91 98765 43210")], Now);

        Assert.Equal([RosterRowOutcome.InvalidPhone, RosterRowOutcome.Added], Outcomes(results));
    }

    [Fact]
    public void ImportMembers_WhenTheBatchFills_TheFirstRowsKeepTheirSeats_AndTheRestAreReported()
    {
        var batch = NewBatch(maxMembers: 2);

        var results = batch.ImportMembers(
            [Row(2, "a@example.com"), Row(3, "b@example.com"), Row(4, "c@example.com"), Row(5, "d@example.com")], Now);

        Assert.Equal(
            [RosterRowOutcome.Added, RosterRowOutcome.Added, RosterRowOutcome.BatchFull, RosterRowOutcome.BatchFull],
            Outcomes(results));
        Assert.Equal(2, batch.GetActiveMemberCount());
    }

    [Fact]
    public void ImportMembers_CountsSeatsAlreadyHeld_AgainstTheCapacity()
    {
        var batch = NewBatch(maxMembers: 2);
        batch.AddMember("first@example.com", null, Now);

        var results = batch.ImportMembers([Row(2, "a@example.com"), Row(3, "b@example.com")], Now);

        Assert.Equal([RosterRowOutcome.Added, RosterRowOutcome.BatchFull], Outcomes(results));
    }

    [Fact]
    public void ImportMembers_SeatsAnAddressWhoseMemberWasRemoved()
    {
        var batch = NewBatch();
        batch.AddMember("amy@example.com", null, Now);
        batch.RemoveMember(Assert.Single(batch.Members).Id, Now);

        var results = batch.ImportMembers([Row(2, "amy@example.com")], Later);

        Assert.Equal(RosterRowOutcome.Added, Assert.Single(results).Outcome);
        Assert.Equal(1, batch.GetActiveMemberCount());
    }

    [Fact]
    public void ImportMembers_RaisesOneEventForTheWholeFile_WithTheIdsOfTheMembersSeated()
    {
        var batch = NewBatch();
        batch.ClearDomainEvents();

        var results = batch.ImportMembers([Row(2, "amy@example.com"), Row(3, "nope"), Row(4, "ben@example.com")], Now);

        var imported = Assert.Single(batch.DomainEvents);
        var @event = Assert.IsType<BatchRosterImportedEvent>(imported);
        Assert.Equal(batch.Id, @event.BatchId);
        Assert.Equal(results.Where(r => r.MemberId is not null).Select(r => r.MemberId!.Value), @event.MemberIds);
    }

    [Fact]
    public void ImportMembers_WhenNoRowIsSeated_RaisesNoEvent_AndLeavesTheBatchUntouched()
    {
        var batch = NewBatch();
        batch.ClearDomainEvents();

        batch.ImportMembers([Row(2, "nope"), Row(3, null)], Later);

        Assert.Empty(batch.DomainEvents);
        Assert.Equal(Now, batch.UpdatedAt);
    }

    [Fact]
    public void ImportMembers_WithANullList_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => NewBatch().ImportMembers(null!, Now));
    }
}
