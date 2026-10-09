using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

public class InviteTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Email = "Candidate@Example.com";
    private static readonly Guid UserId = Guid.NewGuid();

    private static InviteAggregate NewInvite() => new(Guid.NewGuid(), null, Email, Guid.NewGuid(), Now);

    // ---- codes ------------------------------------------------------------------------------------

    [Fact]
    public void NewInvite_IsPending_AndRaisesTheCreatedEvent()
    {
        var invite = NewInvite();

        Assert.Equal(InviteStatus.Pending, invite.Status);
        Assert.Contains(invite.DomainEvents, e => e is InviteCreatedEvent);
        Assert.Null(invite.BatchMemberId);
        Assert.Null(invite.AcceptedByUserId);
    }

    [Fact]
    public void GenerateCode_MakesAnEightCharacterUpperCaseCodeThatExpiresAfterTheLifetime()
    {
        var invite = NewInvite();

        var code = invite.GenerateCode(24, Now);

        Assert.Equal(8, code.Code.Length);
        Assert.Matches("^[A-Z0-9]{8}$", code.Code);
        Assert.Equal(Now.AddHours(24), code.ExpiresAt);
        Assert.Contains(code, invite.Codes);
    }

    [Fact]
    public void GenerateCode_NeverRepeats_AcrossManyCodes()
    {
        var invite = NewInvite();

        var codes = Enumerable.Range(0, 500).Select(_ => invite.GenerateCode(1, Now).Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(InviteAggregate.MaxCodeExpiryHours + 1)]
    public void GenerateCode_WithALifetimeOutOfRange_Throws(int hours)
    {
        var error = Assert.Throws<InvalidInviteExpiryError>(() => NewInvite().GenerateCode(hours, Now));
        Assert.Equal(400, error.HttpStatusCode);
    }

    // ---- accept -----------------------------------------------------------------------------------

    [Fact]
    public void Accept_WithTheRightCodeAndAddress_EnrollsTheCaller()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(72, Now);

        invite.Accept(code.Code, UserId, "candidate@example.com", Now.AddMinutes(5));

        Assert.Equal(InviteStatus.Accepted, invite.Status);
        Assert.Equal(UserId, invite.AcceptedByUserId);
        Assert.Equal(Now.AddMinutes(5), invite.AcceptedAt);
        Assert.Equal(Now.AddMinutes(5), code.UsedAt);
        Assert.Contains(invite.DomainEvents, e => e is InviteAcceptedEvent);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Accept_IgnoresCaseAndSurroundingSpacesInTheCode(bool lower, bool padded)
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(72, Now).Code;
        var typed = (lower ? code.ToLowerInvariant() : code) + (padded ? "  " : "");

        invite.Accept(typed, UserId, Email, Now);

        Assert.Equal(InviteStatus.Accepted, invite.Status);
    }

    [Fact]
    public void Accept_WithAWrongCode_Throws_AndChangesNothing()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(72, Now);

        var error = Assert.Throws<InvalidInviteCodeError>(() => invite.Accept("WRONG123", UserId, Email, Now));

        Assert.Equal("invalid_invite_code", error.ErrorCode);
        Assert.Equal(InviteStatus.Pending, invite.Status);
        Assert.Null(code.UsedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Accept_WithoutACode_Throws(string? code) =>
        Assert.Throws<InvalidInviteCodeError>(() => NewInvite().Accept(code, UserId, Email, Now));

    [Fact]
    public void Accept_AfterTheCodeExpired_Throws()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(1, Now);

        Assert.Throws<InvalidInviteCodeError>(() => invite.Accept(code.Code, UserId, Email, Now.AddHours(1).AddSeconds(1)));
        Assert.Equal(InviteStatus.Pending, invite.Status);
    }

    [Fact]
    public void Accept_OnTheExactExpiryInstant_StillWorks()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(1, Now);

        invite.Accept(code.Code, UserId, Email, Now.AddHours(1));

        Assert.Equal(InviteStatus.Accepted, invite.Status);
    }

    [Fact]
    public void Accept_ForADifferentAddress_Throws403_AndLeavesTheCodeUsable()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(72, Now);

        var error = Assert.Throws<InviteEmailMismatchError>(() => invite.Accept(code.Code, UserId, "someone.else@example.com", Now));

        Assert.Equal(403, error.HttpStatusCode);
        Assert.Null(code.UsedAt);
        Assert.Equal(InviteStatus.Pending, invite.Status);

        // The invited person can still use it.
        invite.Accept(code.Code, Guid.NewGuid(), Email, Now);
        Assert.Equal(InviteStatus.Accepted, invite.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Accept_ByAnAccountWithoutAnEmail_ThrowsMismatch(string? callerEmail)
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(72, Now);

        Assert.Throws<InviteEmailMismatchError>(() => invite.Accept(code.Code, UserId, callerEmail, Now));
    }

    [Fact]
    public void Accept_TwiceWithTheSameCode_ThrowsInvalidCode()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(72, Now);
        invite.Accept(code.Code, UserId, Email, Now);

        Assert.Throws<InvalidInviteCodeError>(() => invite.Accept(code.Code, UserId, Email, Now));
    }

    [Fact]
    public void Accept_ADeclinedInvite_ThrowsStateError()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(72, Now);
        invite.Decline(Now);

        var error = Assert.Throws<InviteStateError>(() => invite.Accept(code.Code, UserId, Email, Now));
        Assert.Equal(409, error.HttpStatusCode);
    }

    // ---- decline and revoke -----------------------------------------------------------------------

    [Fact]
    public void Decline_APendingInvite_Declines()
    {
        var invite = NewInvite();

        invite.Decline(Now);

        Assert.Equal(InviteStatus.Declined, invite.Status);
        Assert.Equal(Now, invite.DeclinedAt);
    }

    [Fact]
    public void Decline_ANonPendingInvite_ThrowsStateError()
    {
        var invite = NewInvite();
        invite.Decline(Now);

        Assert.Throws<InviteStateError>(() => invite.Decline(Now));
    }

    [Fact]
    public void Revoke_RevokesTheInviteAndItsUnusedCodes_AndKeepsAUsedOne()
    {
        var invite = NewInvite();
        var used = invite.GenerateCode(72, Now);
        var unused = invite.GenerateCode(72, Now);
        invite.Accept(used.Code, UserId, Email, Now);

        invite.Revoke(Now.AddMinutes(1));

        Assert.Equal(InviteStatus.Revoked, invite.Status);
        Assert.Null(used.RevokedAt);
        Assert.Equal(Now.AddMinutes(1), unused.RevokedAt);
        Assert.False(unused.IsValid(Now.AddMinutes(2)));
    }

    [Fact]
    public void Revoke_Twice_ThrowsStateError()
    {
        var invite = NewInvite();
        invite.Revoke(Now);

        Assert.Throws<InviteStateError>(() => invite.Revoke(Now));
    }

    [Fact]
    public void Accept_ARevokedInvitesCode_ThrowsInvalidCode()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(72, Now);
        invite.Revoke(Now);

        Assert.Throws<InvalidInviteCodeError>(() => invite.Accept(code.Code, UserId, Email, Now));
    }

    // ---- another code, to hand over by hand -------------------------------------------------------

    [Fact]
    public void AddCode_ToAPendingInvite_MakesACodeThatWorks_AndRaisesAnEventThatNamesItByIdOnly()
    {
        var invite = NewInvite();
        invite.ClearDomainEvents();

        var code = invite.AddCode(24, Now);

        Assert.Contains(code, invite.Codes);
        Assert.Equal(Now.AddHours(24), code.ExpiresAt);
        var raised = Assert.IsType<InviteCodeGeneratedEvent>(Assert.Single(invite.DomainEvents));
        Assert.Equal(invite.Id, raised.InviteId);
        Assert.Equal(invite.ExamId, raised.ExamId);
        Assert.Equal(code.Id, raised.CodeId);

        // The event travels to the audit trail, so it must not carry the credential.
        Assert.DoesNotContain(code.Code, raised.ToString(), StringComparison.Ordinal);

        invite.Accept(code.Code, UserId, Email, Now);
        Assert.Equal(InviteStatus.Accepted, invite.Status);
    }

    [Fact]
    public void TheFirstCode_MadeWithTheInvite_IsNotAnnouncedAsHandedOver()
    {
        var invite = NewInvite();

        invite.GenerateCode(72, Now);

        Assert.DoesNotContain(invite.DomainEvents, e => e is InviteCodeGeneratedEvent);
    }

    [Fact]
    public void AddCode_EachTime_MakesADifferentCode_AndEveryOneWorksUntilTheInviteIsAccepted()
    {
        var invite = NewInvite();

        var codes = Enumerable.Range(0, 5).Select(_ => invite.AddCode(72, Now)).ToList();

        Assert.Equal(5, codes.Select(c => c.Code).Distinct().Count());
        Assert.All(codes, c => Assert.True(c.IsValid(Now)));
        Assert.Equal(5, invite.DomainEvents.OfType<InviteCodeGeneratedEvent>().Count());
    }

    [Theory]
    [InlineData(InviteStatus.Accepted)]
    [InlineData(InviteStatus.Declined)]
    [InlineData(InviteStatus.Revoked)]
    public void AddCode_ToAnInviteNoLongerPending_ThrowsStateError_AndAddsNothing(InviteStatus status)
    {
        var invite = NewInvite();
        var first = invite.GenerateCode(72, Now);
        switch (status)
        {
            case InviteStatus.Accepted:
                invite.Accept(first.Code, UserId, Email, Now);
                break;
            case InviteStatus.Declined:
                invite.Decline(Now);
                break;
            default:
                invite.Revoke(Now);
                break;
        }

        invite.ClearDomainEvents();

        var error = Assert.Throws<InviteStateError>(() => invite.AddCode(72, Now));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Single(invite.Codes);
        Assert.Empty(invite.DomainEvents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InviteAggregate.MaxCodeExpiryHours + 1)]
    public void AddCode_WithALifetimeOutOfRange_ThrowsExpiryError_EvenForAnInviteNoLongerPending(int hours)
    {
        var invite = NewInvite();
        invite.Revoke(Now);

        // A bad lifetime is wrong whatever state the invite is in, so it is reported as that.
        var error = Assert.Throws<InvalidInviteExpiryError>(() => invite.AddCode(hours, Now));

        Assert.Equal(400, error.HttpStatusCode);
    }
}
