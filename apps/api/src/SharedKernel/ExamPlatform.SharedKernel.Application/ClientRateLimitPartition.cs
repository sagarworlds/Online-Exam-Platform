using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// Builds the per-client fixed-window partition that every per-IP rate limiter in the API
/// uses, so the global limit and each module's named policies can never disagree about who
/// counts as "one client" (NFR-5).
/// </summary>
public static class ClientRateLimitPartition
{
    /// <summary>The key shared by every request whose remote address is unknown (e.g. an in-memory test host).</summary>
    public const string UnknownClient = "unknown";

    // A /64 is the smallest block an ISP or hosting provider hands to one subscriber, so a
    // single host can use any of its 2^64 addresses. Keyed by the full address, each one
    // would get a fresh quota and an attacker could rotate through them to dodge every
    // limit, OTP guessing included. The first 8 bytes are the network part of a /64.
    private const int Ipv6NetworkBytes = 8;
    private const int Ipv6PrefixLength = Ipv6NetworkBytes * 8;

    /// <summary>The fixed-window partition for the client that sent the request.</summary>
    /// <param name="httpContext">The request being limited.</param>
    /// <param name="permitLimit">How many requests one client may make per window.</param>
    /// <param name="window">The length of the window.</param>
    public static RateLimitPartition<string> FixedWindow(HttpContext httpContext, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            KeyFor(httpContext.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window });

    /// <summary>
    /// The key that identifies one client: the address itself for IPv4, and the /64 network
    /// for IPv6, so every address a single subscriber controls shares one quota.
    /// </summary>
    /// <param name="address">The request's remote address, if the server knows it.</param>
    public static string KeyFor(IPAddress? address)
    {
        if (address is null)
        {
            return UnknownClient;
        }

        // A dual-stack listener reports an IPv4 client as ::ffff:a.b.c.d: key it as the IPv4
        // address it is, not as a /64 that every IPv4 client on the host would share.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[Ipv6NetworkBytes..].Clear();
        return $"{new IPAddress(bytes)}/{Ipv6PrefixLength}";
    }
}
