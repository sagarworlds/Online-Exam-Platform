using ExamPlatform.Modules.Batch.Domain;

namespace ExamPlatform.Modules.Batch.UnitTests;

/// <summary>
/// The rules a CSV roster is checked against before any member is added: the size limits, one seat per address
/// (compared case-insensitively), and the same address and phone rules a batch applies (FR-50), so an imported
/// roster cannot hold something the batch would later refuse.
/// </summary>
public class RosterValidatorTests
{
    private readonly RosterValidator _validator = new();

    private static RosterValidator.RosterEntry Entry(string email, string? phone = null) => new(email, phone);

    private static List<RosterValidator.RosterEntry> UniqueEntries(int count) =>
        Enumerable.Range(1, count).Select(i => Entry($"candidate{i}@example.com")).ToList();

    [Fact]
    public void ValidateRoster_WithNoRoster_IsInvalidAndSaysSo()
    {
        var result = _validator.ValidateRoster(null);

        Assert.False(result.IsValid);
        Assert.Equal(["Roster cannot be empty."], result.Errors);
    }

    [Fact]
    public void ValidateRoster_WithAnEmptyList_IsInvalidAndSaysSo()
    {
        var result = _validator.ValidateRoster([]);

        Assert.False(result.IsValid);
        Assert.Equal(["Roster cannot be empty."], result.Errors);
    }

    [Fact]
    public void ValidateRoster_WithDistinctValidEntries_IsValid()
    {
        var result = _validator.ValidateRoster([Entry("amy@example.com", "+91 98765 43210"), Entry("ben@example.com")]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateRoster_AtTheMaximumSize_IsValid()
    {
        var result = _validator.ValidateRoster(UniqueEntries(1000));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRoster_OverTheMaximumSize_IsRefused()
    {
        var result = _validator.ValidateRoster(UniqueEntries(1001));

        Assert.False(result.IsValid);
        Assert.Contains("Roster exceeds maximum size of 1000 members.", result.Errors);
    }

    [Fact]
    public void ValidateRoster_WithTheSameAddressInAnotherCase_ReportsTheDuplicate()
    {
        var result = _validator.ValidateRoster([Entry("Amy@Example.com"), Entry("amy@example.COM")]);

        Assert.False(result.IsValid);
        Assert.Contains("Duplicate email in roster: amy@example.com", result.Errors);
    }

    [Fact]
    public void ValidateRoster_WithAMalformedAddress_ReportsIt()
    {
        var result = _validator.ValidateRoster([Entry("not-an-address")]);

        Assert.False(result.IsValid);
        Assert.Contains("Invalid email format: not-an-address", result.Errors);
    }

    [Fact]
    public void ValidateRoster_WithAnAddressThatHasADisplayName_IsRefused()
    {
        // MemberContact refuses "Name <a@b.com>" when a batch adds a member, so the import must refuse it too.
        var result = _validator.ValidateRoster([Entry("Amy <amy@example.com>")]);

        Assert.Contains("Invalid email format: Amy <amy@example.com>", result.Errors);
    }

    [Fact]
    public void ValidateRoster_WithABlankAddress_SaysTheAddressIsRequired()
    {
        var result = _validator.ValidateRoster([Entry("   ")]);

        Assert.False(result.IsValid);
        Assert.Contains("Email is required for all roster entries.", result.Errors);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("+0123456789")]
    [InlineData("98765abc")]
    public void ValidateRoster_WithAnUnusablePhone_ReportsIt(string phone)
    {
        var result = _validator.ValidateRoster([Entry("amy@example.com", phone)]);

        Assert.False(result.IsValid);
        Assert.Contains($"Invalid phone format: {phone}", result.Errors);
    }

    [Fact]
    public void ValidateRoster_WithNoPhone_IsAccepted()
    {
        // A phone is optional, so a blank one is not an error, unlike a blank address.
        var result = _validator.ValidateRoster([Entry("amy@example.com", "   ")]);

        Assert.True(result.IsValid);
    }
}
