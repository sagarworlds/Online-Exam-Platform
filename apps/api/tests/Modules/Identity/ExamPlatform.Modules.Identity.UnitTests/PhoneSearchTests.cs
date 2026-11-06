using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Ports;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>Finding someone by part of their phone number, whichever way the number was written when they registered.</summary>
public class PhoneSearchTests
{
    private static bool Found(string? stored, string typed) => PhoneSearch.Matches(stored, PhoneSearch.Forms(typed));

    [Theory]
    [InlineData("9876543210", "9876543210")]
    [InlineData("9876543210", "98765")] // the start
    [InlineData("9876543210", "43210")] // the end
    [InlineData("9876543210", "65432")] // the middle
    [InlineData("98765 43210", "9876543210")] // spaces in what is stored
    [InlineData("98765-43210", "98765 43210")] // dashes in one, spaces in the other
    [InlineData("(098) 765.43210", "9876543210")] // brackets, a dot and a trunk 0
    [InlineData("+91 98765 43210", "9876543210")] // stored with a country code, typed without
    [InlineData("9876543210", "+91 98765 43210")] // typed with a country code, stored without
    [InlineData("9876543210", "+91 98765")] // a part of it, with a country code
    [InlineData("9876543210", "0091-9876543210")] // 00 for the +
    [InlineData("+919876543210", "919876543210")]
    [InlineData("09876543210", "9876543210")] // a trunk 0 in what is stored
    [InlineData("9876543210", "09876543210")] // and in what is typed
    public void ANumber_IsFound_WhateverWayEachSideWasWritten(string stored, string typed) => Assert.True(Found(stored, typed));

    [Theory]
    [InlineData("9876543210", "9876543211")] // one digit apart
    [InlineData("9876543210", "123")]
    [InlineData("9876543210", "+91 12345")]
    [InlineData("9876543210", "0123")] // the trunk 0 is not part of what is looked for
    public void ANumberThatIsNotInTheStoredOne_IsNotFound(string stored, string typed) => Assert.False(Found(stored, typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("98")] // too few digits to mean anything
    [InlineData("+91")] // a country code alone
    [InlineData("0000")] // nothing but trunk zeros
    [InlineData("student@example.com")] // an e-mail address is not a number
    [InlineData("ab98765")]
    [InlineData("98+76543")] // a plus only means something at the start
    [InlineData("98765x")]
    public void WhatIsNotAPhoneNumber_StandsForNothing(string typed) => Assert.Empty(PhoneSearch.Forms(typed));

    [Fact]
    public void WhatIsNotAPhoneNumber_MatchesNoOne() => Assert.False(Found("9876543210", "student@example.com"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AnAccountWithNoNumber_IsNeverFound(string? stored) => Assert.False(Found(stored, "98765"));

    [Fact]
    public void AStoredValueThatIsNotANumber_IsNeverFound() => Assert.False(Found("call after 5", "98765"));

    [Fact]
    public void TheCountryCodeIsOnlyDroppedFromASearchThatWritesOne()
    {
        // 91 is a country code only when the search says so; "91234" typed plainly is looked for as it stands.
        Assert.Equal(["91234"], PhoneSearch.Forms("91234"));
        // A country code is one to three digits and the search does not say which, so each is tried, while at least three digits stay.
        Assert.Equal(["9198765", "198765", "98765", "8765"], PhoneSearch.Forms("+91 98765"));
        Assert.Equal(["919876", "19876", "9876", "876"], PhoneSearch.Forms("0091 9876"));
    }
}

/// <summary>The question other modules ask Identity: which of these accounts hold a number that matches. It answers with ids, never numbers.</summary>
public class AccountPhoneSearchTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly AccountPhoneSearch _search;

    public AccountPhoneSearchTests()
    {
        _search = new AccountPhoneSearch(_users);
    }

    private void Numbers(params (Guid Id, string Phone)[] numbers) =>
        _users.ListPhoneNumbersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, string>>(numbers.ToDictionary(n => n.Id, n => n.Phone)));

    [Fact]
    public async Task TheAccountsWhoseNumberMatches_AreReturned_AndTheOthersAreNot()
    {
        Guid ravi = Guid.NewGuid(), meena = Guid.NewGuid(), noNumber = Guid.NewGuid();
        Numbers((ravi, "+91 98765 43210"), (meena, "70000 11111"));

        var found = await _search.FindAccountsByPhoneAsync([ravi, meena, noNumber], "98765-43", CancellationToken.None);

        Assert.Equal([ravi], found);
    }

    [Fact]
    public async Task OnlyTheAccountsThatWereAskedAbout_AreLookedThrough()
    {
        var asked = Guid.NewGuid();

        await _search.FindAccountsByPhoneAsync([asked], "98765", CancellationToken.None);

        await _users.Received(1).ListPhoneNumbersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(asked)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("98")]
    [InlineData("student@example.com")]
    public async Task ASearchThatIsNotANumber_ReadsNothingAndMatchesNoOne(string typed)
    {
        var found = await _search.FindAccountsByPhoneAsync([Guid.NewGuid()], typed, CancellationToken.None);

        Assert.Empty(found);
        await _users.DidNotReceive().ListPhoneNumbersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoAccountsAskedAbout_ReadsNothing()
    {
        var found = await _search.FindAccountsByPhoneAsync([], "98765", CancellationToken.None);

        Assert.Empty(found);
        await _users.DidNotReceive().ListPhoneNumbersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }
}
