using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// What the shared e-mail rule accepts. These pin the behaviour the modules had before they shared it, quirks included, so that moving
/// them onto one rule changed no answer; a change to any row here is a change to which addresses are accepted.
/// </summary>
public class EmailAddressRuleTests
{
    [Theory]
    [InlineData("a@b.in")]
    [InlineData("first.last+tag@sub.example.com")]
    [InlineData("a@b")]                        // no dot after the @: the framework allows it
    [InlineData("a b@x.in")]                   // white space inside: the framework allows it, so IsDeliverable must not
    [InlineData(" a@b.in")]
    [InlineData("a@b.in ")]
    [InlineData("a\t@b.in")]
    public void WellFormed_IsTheFrameworksOwnCheck(string address) => Assert.True(EmailAddressRule.IsWellFormed(address));

    [Theory]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("@b.in")]                      // nothing before the @
    [InlineData("a@")]                         // nothing after it
    [InlineData("a@@b.in")]
    [InlineData("a@b@c.in")]
    public void WellFormed_RefusesWhatIsNotShapedLikeAnAddress(string address) => Assert.False(EmailAddressRule.IsWellFormed(address));

    [Fact]
    public void WellFormed_CountsNullAsValid_LikeTheFramework() => Assert.True(EmailAddressRule.IsWellFormed(null));

    [Theory]
    [InlineData("a@b.in")]
    [InlineData("first.last+tag@sub.example.com")]
    [InlineData("a@b")]
    public void Deliverable_AcceptsAnAddressWithoutWhiteSpace(string address) => Assert.True(EmailAddressRule.IsDeliverable(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b@x.in")]
    [InlineData(" a@b.in")]
    [InlineData("a@b.in ")]
    [InlineData("a\t@b.in")]
    [InlineData("a@b.in\n")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@b.in")]
    [InlineData("a@")]
    public void Deliverable_RefusesMissingMalformedAndSpacedAddresses(string? address) => Assert.False(EmailAddressRule.IsDeliverable(address));
}
