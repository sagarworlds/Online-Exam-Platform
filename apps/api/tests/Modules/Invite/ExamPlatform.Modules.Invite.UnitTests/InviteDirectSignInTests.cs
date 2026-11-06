using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>
/// The direct sign-in code: a code made for signing the invited person in as well as accepting, which only such a code may do. An ordinary
/// code must never sign anyone in, because it is also typed in, read out and shown to staff.
/// </summary>
public class InviteDirectSignInTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private static InviteAggregate NewInvite() => new(Guid.NewGuid(), null, "student@example.com", Guid.NewGuid(), Now);

    [Fact]
    public void GenerateCode_ForDirectSignIn_MakesASixteenCharacterCode_AndAnnouncesItByIdOnly()
    {
        var invite = NewInvite();

        var code = invite.GenerateCode(72, Now, allowsDirectSignIn: true);

        Assert.True(code.AllowsDirectSignIn);
        Assert.Matches("^[A-Z0-9]{16}$", code.Code);
        var issued = Assert.Single(invite.DomainEvents.OfType<InviteDirectLinkIssuedEvent>());
        Assert.Equal(code.Id, issued.CodeId);
        Assert.DoesNotContain(code.Code, issued.ToString());
    }

    [Fact]
    public void GenerateCode_Ordinary_StaysEightCharacters_CannotSignIn_AndIsNotAnnouncedAsDirect()
    {
        var invite = NewInvite();

        var code = invite.GenerateCode(72, Now);

        Assert.False(code.AllowsDirectSignIn);
        Assert.Equal(8, code.Code.Length);
        Assert.DoesNotContain(invite.DomainEvents, e => e is InviteDirectLinkIssuedEvent);
    }

    [Theory]
    [InlineData(InviteAggregate.MaxDirectCodeExpiryHours + 1)]
    [InlineData(InviteAggregate.MaxCodeExpiryHours)]
    public void GenerateCode_ForDirectSignIn_RefusesALifetimeLongerThanItsOwnLimit(int hours)
    {
        // A direct code is a login, so it may live less long than an ordinary code, which keeps the 30 days.
        var error = Assert.Throws<InvalidInviteExpiryError>(() => NewInvite().GenerateCode(hours, Now, allowsDirectSignIn: true));

        Assert.Equal(400, error.HttpStatusCode);
        Assert.Contains(InviteAggregate.MaxDirectCodeExpiryHours.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message);
    }

    [Fact]
    public void GenerateCode_Ordinary_StillAllowsTheLongLifetime() =>
        Assert.NotNull(NewInvite().GenerateCode(InviteAggregate.MaxCodeExpiryHours, Now));

    [Fact]
    public void AddCode_ForDirectSignIn_RaisesTheDirectEventOnly_NotAlsoTheHandedOverOne()
    {
        var invite = NewInvite();

        invite.AddCode(24, Now, allowsDirectSignIn: true);

        Assert.Single(invite.DomainEvents.OfType<InviteDirectLinkIssuedEvent>());
        Assert.DoesNotContain(invite.DomainEvents, e => e is InviteCodeGeneratedEvent);
    }

    [Fact]
    public void EnsureDirectSignInPossible_ForADirectCode_ReturnsIt_IgnoringCaseAndSpaces()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(24, Now, allowsDirectSignIn: true);

        var found = invite.EnsureDirectSignInPossible($"  {code.Code.ToLowerInvariant()} ", Now);

        Assert.Same(code, found);
    }

    [Fact]
    public void EnsureDirectSignInPossible_ForAnOrdinaryCode_ThrowsInvalidCode_LikeAnUnknownOne()
    {
        var invite = NewInvite();
        var ordinary = invite.GenerateCode(24, Now);

        var refused = Assert.Throws<InvalidInviteCodeError>(() => invite.EnsureDirectSignInPossible(ordinary.Code, Now));
        var unknown = Assert.Throws<InvalidInviteCodeError>(() => invite.EnsureDirectSignInPossible("ZZZZZZZZZZZZZZZZ", Now));

        // The two cannot be told apart, so a caller who guesses learns nothing from which one they got.
        Assert.Equal(unknown.Message, refused.Message);
        Assert.Equal(unknown.ErrorCode, refused.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureDirectSignInPossible_WithoutACode_ThrowsInvalidCode(string? code) =>
        Assert.Throws<InvalidInviteCodeError>(() => NewInvite().EnsureDirectSignInPossible(code, Now));

    [Fact]
    public void EnsureDirectSignInPossible_AfterTheCodeExpired_ThrowsInvalidCode()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(1, Now, allowsDirectSignIn: true);

        Assert.Throws<InvalidInviteCodeError>(() => invite.EnsureDirectSignInPossible(code.Code, Now.AddHours(1).AddSeconds(1)));
    }

    [Fact]
    public void EnsureDirectSignInPossible_ForARevokedCode_ThrowsInvalidCode()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(24, Now, allowsDirectSignIn: true);
        invite.Revoke(Now);

        Assert.Throws<InvalidInviteCodeError>(() => invite.EnsureDirectSignInPossible(code.Code, Now));
    }

    [Fact]
    public void EnsureDirectSignInPossible_ChangesNothing()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(24, Now, allowsDirectSignIn: true);

        invite.EnsureDirectSignInPossible(code.Code, Now);

        // It runs before the account is looked up or made, so a refusal there must leave the code usable.
        Assert.Equal(InviteStatus.Pending, invite.Status);
        Assert.Null(code.UsedAt);
        Assert.Null(invite.AcceptedByUserId);
    }

    [Fact]
    public void AcceptByDirectLink_AcceptsTheInvite_ForTheAccount_AndUsesUpTheCode()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(24, Now, allowsDirectSignIn: true);
        var later = Now.AddMinutes(5);

        invite.AcceptByDirectLink(code.Code, UserId, accountCreated: true, later);

        Assert.Equal(InviteStatus.Accepted, invite.Status);
        Assert.Equal(UserId, invite.AcceptedByUserId);
        Assert.Equal(later, invite.AcceptedAt);
        Assert.Equal(later, code.UsedAt);
    }

    [Fact]
    public void AcceptByDirectLink_RaisesOneEventThatNamesTheAccount_InsteadOfTheOrdinaryAcceptedEvent()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(24, Now, allowsDirectSignIn: true);

        invite.AcceptByDirectLink(code.Code, UserId, accountCreated: true, Now);

        var used = Assert.Single(invite.DomainEvents.OfType<InviteDirectLinkUsedEvent>());
        Assert.Equal(UserId, used.UserId);
        Assert.Equal(code.Id, used.CodeId);
        Assert.True(used.AccountCreated);
        Assert.DoesNotContain(invite.DomainEvents, e => e is InviteAcceptedEvent);
    }

    [Fact]
    public void AcceptByDirectLink_Twice_ThrowsInvalidCode_SoALinkSignsInOnlyOnce()
    {
        var invite = NewInvite();
        var code = invite.GenerateCode(24, Now, allowsDirectSignIn: true);
        invite.AcceptByDirectLink(code.Code, UserId, accountCreated: false, Now);

        Assert.Throws<InvalidInviteCodeError>(() => invite.AcceptByDirectLink(code.Code, UserId, accountCreated: false, Now));
    }

    [Fact]
    public void AcceptByDirectLink_WithAnOrdinaryCode_Throws_AndChangesNothing()
    {
        var invite = NewInvite();
        var ordinary = invite.GenerateCode(24, Now);

        Assert.Throws<InvalidInviteCodeError>(() => invite.AcceptByDirectLink(ordinary.Code, UserId, accountCreated: false, Now));

        Assert.Equal(InviteStatus.Pending, invite.Status);
        Assert.Null(ordinary.UsedAt);
    }

    [Fact]
    public void AcceptByDirectLink_OnAnInviteAlreadyAcceptedByAnotherCode_ThrowsStateError()
    {
        var invite = NewInvite();
        var ordinary = invite.GenerateCode(24, Now);
        var direct = invite.GenerateCode(24, Now, allowsDirectSignIn: true);
        invite.Accept(ordinary.Code, UserId, "student@example.com", Now);

        var error = Assert.Throws<InviteStateError>(() => invite.AcceptByDirectLink(direct.Code, UserId, accountCreated: false, Now));

        Assert.Equal(409, error.HttpStatusCode);
    }

    [Fact]
    public void ChangeEmail_RevokesUnusedDirectCodes_AndLeavesOrdinaryOnesUsable()
    {
        var invite = NewInvite();
        var ordinary = invite.GenerateCode(24, Now);
        var direct = invite.GenerateCode(24, Now, allowsDirectSignIn: true);
        var later = Now.AddMinutes(3);

        invite.ChangeEmail("new@example.com", later);

        // A direct link signs in whoever owns the invite's address, so one sent to the old address must stop working.
        Assert.Equal(later, direct.RevokedAt);
        Assert.Throws<InvalidInviteCodeError>(() => invite.EnsureDirectSignInPossible(direct.Code, later));
        Assert.Null(ordinary.RevokedAt);
        Assert.True(ordinary.IsValid(later));
    }

    [Fact]
    public void ChangeEmail_KeepsTheTimeAUsedDirectCodeWasUsed()
    {
        var invite = NewInvite();
        var direct = invite.GenerateCode(24, Now, allowsDirectSignIn: true);
        invite.AcceptByDirectLink(direct.Code, UserId, accountCreated: false, Now);

        invite.ChangeEmail("new@example.com", Now.AddHours(1));

        Assert.Null(direct.RevokedAt);
        Assert.Equal(Now, direct.UsedAt);
    }
}
