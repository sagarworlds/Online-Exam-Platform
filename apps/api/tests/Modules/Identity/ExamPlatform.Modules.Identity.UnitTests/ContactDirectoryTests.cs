using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class ContactDirectoryTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private ContactDirectory Directory => new(_users);

    private static User Account(string? phone, bool active = true)
    {
        var user = User.Register("amy@example.com", phone, new DateOnly(1995, 1, 1), "Amy", Now);
        if (active)
        {
            user.Activate();
        }

        return user;
    }

    [Fact]
    public async Task TheActiveAccountsPhoneNumberIsReturnedAsEntered()
    {
        _users.GetByEmailAsync("amy@example.com", Arg.Any<CancellationToken>()).Returns(Account("98765 43210"));

        Assert.Equal("98765 43210", await Directory.FindPhoneNumberByEmailAsync("amy@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task SpacesAroundTheAddressAreIgnored()
    {
        _users.GetByEmailAsync("amy@example.com", Arg.Any<CancellationToken>()).Returns(Account("9876543210"));

        Assert.Equal("9876543210", await Directory.FindPhoneNumberByEmailAsync("  amy@example.com ", CancellationToken.None));
    }

    [Fact]
    public async Task NoAccountForTheAddressMeansNoNumber()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        Assert.Null(await Directory.FindPhoneNumberByEmailAsync("nobody@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task AnAccountWithoutAPhoneNumberHasNoNumberToGive()
    {
        _users.GetByEmailAsync("amy@example.com", Arg.Any<CancellationToken>()).Returns(Account(phone: null));

        Assert.Null(await Directory.FindPhoneNumberByEmailAsync("amy@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task AnAccountNotYetVerifiedHasNoNumberToGive()
    {
        _users.GetByEmailAsync("amy@example.com", Arg.Any<CancellationToken>()).Returns(Account("9876543210", active: false));

        Assert.Null(await Directory.FindPhoneNumberByEmailAsync("amy@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task ASuspendedAccountHasNoNumberToGive()
    {
        var suspended = Account("9876543210");
        suspended.Suspend(Now);
        _users.GetByEmailAsync("amy@example.com", Arg.Any<CancellationToken>()).Returns(suspended);

        Assert.Null(await Directory.FindPhoneNumberByEmailAsync("amy@example.com", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankAddressIsAnsweredWithNoNumberWithoutAskingTheRepository(string email)
    {
        Assert.Null(await Directory.FindPhoneNumberByEmailAsync(email, CancellationToken.None));

        await _users.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
