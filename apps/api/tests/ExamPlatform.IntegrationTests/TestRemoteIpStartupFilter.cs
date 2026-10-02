using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Gives an in-memory test request the remote address a real connection would have. The
/// TestServer leaves <c>Connection.RemoteIpAddress</c> null, which is why per-IP behaviour
/// (rate-limit partitions, forwarded-header trust) is invisible to a plain test client.
/// The address is read from the <see cref="HeaderName"/> request header; a request without it
/// is untouched.
/// </summary>
/// <remarks>
/// Must run before every other middleware, including the forwarded-headers one that the
/// framework's own startup filter adds, because a real server sets the address before any of
/// them runs. <see cref="Register"/> puts it first for that reason.
/// </remarks>
internal sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    /// <summary>The request header that carries the simulated remote address.</summary>
    public const string HeaderName = "X-Test-Remote-Ip";

    /// <summary>Makes <paramref name="services"/> run this filter ahead of all other startup filters.</summary>
    /// <param name="services">The host's service collection.</param>
    public static void Register(IServiceCollection services) =>
        services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, TestRemoteIpStartupFilter>());

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var value) && IPAddress.TryParse(value.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware(context);
        });

        next(app);
    };
}
