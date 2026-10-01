using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Api;

/// <summary>
/// Configures <see cref="ForwardedHeadersOptions"/> so the API sees the real client IP and
/// scheme when it runs behind a reverse proxy or load balancer (NFR-5). Per-IP rate limits
/// and the session's recorded IP would otherwise all see the proxy's address.
/// </summary>
/// <remarks>
/// Only <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are honoured, and only from
/// proxies listed in <c>ForwardedHeaders:KnownProxies</c> (addresses) or
/// <c>ForwardedHeaders:KnownNetworks</c> (CIDR ranges, e.g. <c>10.0.0.0/8</c>). Both lists
/// default to empty, which leaves ASP.NET Core's default of trusting loopback only. That is
/// deliberate: a header from an untrusted sender must be ignored, or a client could choose
/// its own rate-limit partition by sending <c>X-Forwarded-For</c>.
/// </remarks>
/// <param name="configuration">The application configuration the lists are read from.</param>
internal sealed class ForwardedHeadersOptionsSetup(IConfiguration configuration) : IConfigureOptions<ForwardedHeadersOptions>
{
    private const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";
    private const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    /// <inheritdoc />
    public void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // One hop: the nearest proxy. Walking further back would let a client that can
        // append to X-Forwarded-For pick which address is treated as the caller's.
        options.ForwardLimit = 1;

        foreach (var proxy in ReadList(KnownProxiesKey))
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException($"{KnownProxiesKey} contains '{proxy}', which is not an IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in ReadList(KnownNetworksKey))
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException(
                    $"{KnownNetworksKey} contains '{network}', which is not a CIDR range such as 10.0.0.0/8.");
            }

            options.KnownIPNetworks.Add(parsed);
        }
    }

    // A list, not a single value: the configuration binder drops a scalar where it expects an
    // array (e.g. an environment variable set without an index), which would silently leave
    // the proxy untrusted and every client behind it sharing one rate-limit partition (NFR-5).
    private string[] ReadList(string key)
    {
        var section = configuration.GetSection(key);
        if (!string.IsNullOrWhiteSpace(section.Value))
        {
            throw new InvalidOperationException(
                $"{key} must be a list, but is the single value '{section.Value}'. "
                + $"Use a JSON array, or indexed environment variables such as {key.Replace(":", "__")}__0.");
        }

        return section.Get<string[]>() ?? [];
    }
}
