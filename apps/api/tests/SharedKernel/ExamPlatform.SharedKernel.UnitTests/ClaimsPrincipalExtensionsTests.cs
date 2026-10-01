using System.Security.Claims;
using ExamPlatform.SharedKernel.Application.Security;

namespace ExamPlatform.SharedKernel.UnitTests;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void GetUserId_WithGuidSubClaim_ReturnsIt()
    {
        var userId = Guid.NewGuid();
        var user = PrincipalWith(new Claim("sub", userId.ToString()));

        Assert.Equal(userId, user.GetUserId());
    }

    [Fact]
    public void GetUserId_WithoutSubClaim_ThrowsMissingAuthenticatedUserError()
    {
        var user = PrincipalWith(new Claim("sid", Guid.NewGuid().ToString()));

        var error = Assert.Throws<MissingAuthenticatedUserError>(() => user.GetUserId());

        Assert.Equal("sub", error.ClaimType);
    }

    [Fact]
    public void GetUserId_WithAnonymousPrincipal_ThrowsMissingAuthenticatedUserError()
    {
        Assert.Throws<MissingAuthenticatedUserError>(() => new ClaimsPrincipal(new ClaimsIdentity()).GetUserId());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void GetUserId_WithNonGuidOrEmptySub_ThrowsMissingAuthenticatedUserError(string sub)
    {
        var user = PrincipalWith(new Claim("sub", sub));

        Assert.Throws<MissingAuthenticatedUserError>(() => user.GetUserId());
    }

    [Fact]
    public void GetUserId_ReadsTheLiteralSubClaim_NotTheMappedNameIdentifier()
    {
        // With inbound claim mapping off (the Host's setting) the token's "sub" stays "sub"; a
        // NameIdentifier claim is not an identity this application issued.
        var user = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        Assert.Throws<MissingAuthenticatedUserError>(() => user.GetUserId());
    }

    [Fact]
    public void GetSessionId_WithGuidSidClaim_ReturnsIt()
    {
        var sessionId = Guid.NewGuid();
        var user = PrincipalWith(new Claim("sid", sessionId.ToString()));

        Assert.Equal(sessionId, user.GetSessionId());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void GetSessionId_WithMissingOrInvalidSid_ThrowsMissingAuthenticatedUserError(string? sid)
    {
        var claims = sid is null ? [] : new[] { new Claim("sid", sid) };
        var user = PrincipalWith(claims);

        var error = Assert.Throws<MissingAuthenticatedUserError>(() => user.GetSessionId());

        Assert.Equal("sid", error.ClaimType);
    }

    [Fact]
    public void GetPrimaryRole_ReadsClaimTypesRoleOrShortRole_ElseUnknown()
    {
        Assert.Equal("Candidate", PrincipalWith(new Claim(ClaimTypes.Role, "Candidate")).GetPrimaryRole());
        Assert.Equal("ExamAdmin", PrincipalWith(new Claim("role", "ExamAdmin")).GetPrimaryRole());
        Assert.Equal("Unknown", PrincipalWith().GetPrimaryRole());
    }

    [Fact]
    public void GetPrimaryRole_PrefersTheClaimTypesRoleOverTheShortName()
    {
        var user = PrincipalWith(new Claim("role", "short"), new Claim(ClaimTypes.Role, "long"));

        Assert.Equal("long", user.GetPrimaryRole());
    }

    [Fact]
    public void GetPrimaryRole_WithBlankRole_ReturnsUnknown()
    {
        Assert.Equal("Unknown", PrincipalWith(new Claim(ClaimTypes.Role, "  ")).GetPrimaryRole());
    }

    [Fact]
    public void HasPermission_WithMatchingPermClaim_ReturnsTrue()
    {
        var user = PrincipalWith(new Claim("perm", "batch.manage"), new Claim("perm", "batch.read"));

        Assert.True(user.HasPermission("batch.manage"));
        Assert.True(user.HasPermission("batch.read"));
    }

    [Fact]
    public void HasPermission_WithoutMatchingClaim_ReturnsFalse()
    {
        var user = PrincipalWith(new Claim("perm", "batch.read"));

        Assert.False(user.HasPermission("batch.manage"));
    }

    [Fact]
    public void HasPermission_IsCaseSensitiveAndIgnoresOtherClaimTypes()
    {
        // A role or any other claim that merely carries the same text must not grant access.
        var user = PrincipalWith(new Claim("perm", "batch.read"), new Claim(ClaimTypes.Role, "batch.manage"));

        Assert.False(user.HasPermission("BATCH.READ"));
        Assert.False(user.HasPermission("batch.manage"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void HasPermission_WithBlankCode_ReturnsFalse(string? code)
    {
        // Fails closed even for a token that (maliciously or by mistake) carries an empty perm claim.
        var user = PrincipalWith(new Claim("perm", ""), new Claim("perm", " "));

        Assert.False(user.HasPermission(code));
    }

    [Fact]
    public void MissingAuthenticatedUserError_IsAnUnauthorizedDomainError()
    {
        var error = new MissingAuthenticatedUserError("sub");

        Assert.Equal(401, error.HttpStatusCode);
        Assert.Equal("unauthenticated", error.ErrorCode);
        Assert.Contains("'sub'", error.Message);
    }
}
