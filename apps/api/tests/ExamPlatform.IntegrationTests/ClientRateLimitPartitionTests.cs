using System.Net;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Pins who counts as "one client" for every per-IP rate limit (NFR-5): an IPv4 address is
/// one client, and so is a whole IPv6 /64, because a single subscriber controls all of it.
/// </summary>
public sealed class ClientRateLimitPartitionTests
{
    [Fact]
    public void KeyFor_UnknownAddress_IsTheSharedUnknownKey()
    {
        Assert.Equal(ClientRateLimitPartition.UnknownClient, ClientRateLimitPartition.KeyFor(null));
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]

    // A dual-stack listener reports IPv4 clients in IPv6 form; they must not collapse into one /64.
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.8", "203.0.113.8")]
    public void KeyFor_IPv4_IsTheAddressItself(string address, string expected)
    {
        Assert.Equal(expected, ClientRateLimitPartition.KeyFor(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("2001:db8:1:2::1")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff")]
    [InlineData("2001:DB8:1:2::")]
    public void KeyFor_AddressesInOneIPv6Slash64_ShareAKey(string address)
    {
        Assert.Equal("2001:db8:1:2::/64", ClientRateLimitPartition.KeyFor(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("2001:db8:1:3::1")]
    [InlineData("2001:db8:2:2::1")]
    [InlineData("2001:db9:1:2::1")]
    public void KeyFor_AddressesInDifferentIPv6Slash64s_GetDifferentKeys(string other)
    {
        var inFirstNetwork = ClientRateLimitPartition.KeyFor(IPAddress.Parse("2001:db8:1:2::1"));

        Assert.NotEqual(inFirstNetwork, ClientRateLimitPartition.KeyFor(IPAddress.Parse(other)));
    }

    [Fact]
    public void KeyFor_IPv6WithAScopeId_IgnoresTheScope()
    {
        // A link-local address carries the interface it arrived on; it is the same client either way.
        Assert.Equal(
            ClientRateLimitPartition.KeyFor(IPAddress.Parse("fe80::1")),
            ClientRateLimitPartition.KeyFor(IPAddress.Parse("fe80::1%5")));
    }
}
