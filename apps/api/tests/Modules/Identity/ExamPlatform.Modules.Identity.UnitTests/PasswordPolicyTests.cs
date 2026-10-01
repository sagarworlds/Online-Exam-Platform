using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Exceptions;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class PasswordPolicyTests
{
    private const string Email = "priya.sharma@example.com";

    private readonly PasswordPolicy _policy = new();

    public static TheoryData<string?> WeakPasswords => new()
    {
        null,
        "",
        new string('a', PasswordPolicy.MinLength - 1),
        new string('a', PasswordPolicy.MaxLength + 1),
        new string(' ', PasswordPolicy.MinLength),
        "\t\t\t\t      \n\n",
        "my name is priya.sharma",
        "MY NAME IS PRIYA.SHARMA",
    };

    [Theory]
    [MemberData(nameof(WeakPasswords))]
    public void EnsureAcceptable_WeakPassword_ThrowsWeakPasswordError(string? password)
    {
        var error = Assert.Throws<WeakPasswordError>(() => _policy.EnsureAcceptable(password, Email));

        Assert.Equal("weak_password", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("exactly12chr")]
    public void EnsureAcceptable_LongEnoughPassphrase_Passes(string password) =>
        _policy.EnsureAcceptable(password, Email);

    [Fact]
    public void EnsureAcceptable_AtTheMaximumLength_Passes() =>
        _policy.EnsureAcceptable(new string('a', PasswordPolicy.MaxLength), Email);

    [Fact]
    public void EnsureAcceptable_EmailLocalPartShorterThanChecked_IsIgnored() =>
        _policy.EnsureAcceptable("ab-is-my-whole-name", "ab@example.com");

    [Fact]
    public void EnsureAcceptable_WithoutEmail_ChecksOnlyLength() =>
        _policy.EnsureAcceptable("correct horse battery staple", email: null);

    [Fact]
    public void EnsureAcceptable_ContainsTheDomainOnly_Passes() =>
        _policy.EnsureAcceptable("example.com is where I work", Email);
}
