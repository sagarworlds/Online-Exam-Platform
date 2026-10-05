using ExamPlatform.Modules.Batch.Domain;
using ExamPlatform.Modules.Batch.Domain.Exceptions;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

public class BatchTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static BatchAggregate NewBatch(int maxMembers = 10) =>
        new(Guid.NewGuid(), "Batch A", null, maxMembers, Guid.NewGuid(), Now);

    [Fact]
    public void Create_StampsTheGivenInstant_AndStartsPending()
    {
        var batch = NewBatch();

        Assert.Equal(Now, batch.CreatedAt);
        Assert.Equal(Now, batch.UpdatedAt);
        Assert.Equal(BatchStatus.Pending, batch.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_WithNonPositiveCapacity_ThrowsInvalidBatchConfigError(int maxMembers)
    {
        Assert.Throws<InvalidBatchConfigError>(() => NewBatch(maxMembers));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ThrowsInvalidBatchConfigError(string name)
    {
        Assert.Throws<InvalidBatchConfigError>(() => new BatchAggregate(Guid.NewGuid(), name, null, 5, Guid.NewGuid(), Now));
    }

    [Fact]
    public void AddMember_StoresTheAddressTrimmedAndLowerCased()
    {
        var batch = NewBatch();

        batch.AddMember("  Jane.Doe@Example.COM ", null, Now);

        Assert.Equal("jane.doe@example.com", Assert.Single(batch.Members).Email);
    }

    [Fact]
    public void AddMember_StoresThePhoneWithoutSpacesOrDashes()
    {
        var batch = NewBatch();

        batch.AddMember("a@example.com", "+91-98765 43210", Now);

        Assert.Equal("+919876543210", Assert.Single(batch.Members).Phone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Jane <jane@example.com>")]
    public void AddMember_WithAnUnusableAddress_ThrowsInvalidBatchMemberError(string email)
    {
        Assert.Throws<InvalidBatchMemberError>(() => NewBatch().AddMember(email, null, Now));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789")]
    [InlineData("+12345678901234567890123")]
    public void AddMember_WithAnUnusablePhone_ThrowsInvalidBatchMemberError(string phone)
    {
        Assert.Throws<InvalidBatchMemberError>(() => NewBatch().AddMember("a@example.com", phone, Now));
    }

    [Fact]
    public void AddMember_SameAddressInAnotherCase_ThrowsDuplicateMemberError()
    {
        var batch = NewBatch();
        batch.AddMember("a@example.com", null, Now);

        Assert.Throws<DuplicateMemberError>(() => batch.AddMember("A@Example.com", null, Now));
    }

    [Fact]
    public void DuplicateMemberError_Message_DoesNotContainTheAddress()
    {
        var batch = NewBatch();
        batch.AddMember("secret.person@example.com", null, Now);

        var error = Assert.Throws<DuplicateMemberError>(() => batch.AddMember("secret.person@example.com", null, Now));

        Assert.DoesNotContain("secret.person", error.Message);
    }

    [Fact]
    public void AddMember_AfterTheMemberWasRemoved_GivesTheSeatAgain()
    {
        var batch = NewBatch();
        batch.AddMember("a@example.com", null, Now);
        batch.RemoveMember(batch.Members[0].Id, Now);

        batch.AddMember("a@example.com", null, Now);

        Assert.Equal(1, batch.GetActiveMemberCount());
    }

    [Fact]
    public void AddMember_AtCapacity_ThrowsInvalidBatchConfigError()
    {
        var batch = NewBatch(maxMembers: 1);
        batch.AddMember("a@example.com", null, Now);

        Assert.Throws<InvalidBatchConfigError>(() => batch.AddMember("b@example.com", null, Now));
    }

    [Fact]
    public void RemoveMember_Unknown_ThrowsBatchMemberNotFoundError()
    {
        Assert.Throws<BatchMemberNotFoundError>(() => NewBatch().RemoveMember(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Activate_WithoutMembers_ThrowsInvalidBatchConfigError()
    {
        Assert.Throws<InvalidBatchConfigError>(() => NewBatch().Activate(Now));
    }

    [Fact]
    public void Activate_ThenClose_MovesThroughTheLifecycle_AndStampsEachStep()
    {
        var batch = NewBatch();
        batch.AddMember("a@example.com", null, Now);
        var later = Now.AddHours(1);

        batch.Activate(later);
        Assert.Equal(BatchStatus.Active, batch.Status);
        Assert.Equal(later, batch.UpdatedAt);

        var muchLater = Now.AddHours(2);
        batch.Close(muchLater);
        Assert.Equal(BatchStatus.Closed, batch.Status);
        Assert.Equal(muchLater, batch.UpdatedAt);
    }

    [Fact]
    public void Activate_Twice_ThrowsInvalidBatchConfigError()
    {
        var batch = NewBatch();
        batch.AddMember("a@example.com", null, Now);
        batch.Activate(Now);

        Assert.Throws<InvalidBatchConfigError>(() => batch.Activate(Now));
    }

    [Fact]
    public void Close_WhenPending_ThrowsInvalidBatchConfigError()
    {
        Assert.Throws<InvalidBatchConfigError>(() => NewBatch().Close(Now));
    }
}
