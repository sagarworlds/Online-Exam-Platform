using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>One HTTP route the running API exposes, and the authorization policies declared on it.</summary>
/// <param name="Method">The HTTP method, upper case (e.g. <c>POST</c>).</param>
/// <param name="Pattern">The route template with any trailing slash removed (e.g. <c>/v1/batches/{batchId}/close</c>).</param>
/// <param name="RequiresAuthorization">Whether the route carries any authorization requirement at all.</param>
/// <param name="Policies">The named policies on the route, from its own metadata and every enclosing group.</param>
public sealed record RouteAuthorization(
    string Method,
    string Pattern,
    bool RequiresAuthorization,
    IReadOnlyList<string> Policies)
{
    private const string PermissionPolicyPrefix = "permission:";

    /// <summary>The route as <c>METHOD pattern</c>, the form tests use to name it.</summary>
    public string Key => $"{Method} {Pattern}";

    /// <summary>The permission codes this route demands, taken from its <c>permission:{code}</c> policies.</summary>
    public IEnumerable<string> PermissionCodes => Policies
        .Where(p => p.StartsWith(PermissionPolicyPrefix, StringComparison.Ordinal))
        .Select(p => p[PermissionPolicyPrefix.Length..]);
}

/// <summary>
/// Reads the authorization metadata of every endpoint the running API has mapped, so tests can
/// assert on the access rules the host really applies instead of on a copy of them.
/// </summary>
public static class EndpointAuthorizationInspector
{
    /// <summary>Lists every route of the host (one entry per HTTP method) with its authorization policies.</summary>
    /// <param name="services">The host's service provider, e.g. <c>factory.Services</c>.</param>
    public static IReadOnlyList<RouteAuthorization> ListRoutes(IServiceProvider services)
    {
        var dataSource = services.GetRequiredService<EndpointDataSource>();
        var routes = new List<RouteAuthorization>();

        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var policies = authorizeData
                .Select(a => a.Policy)
                .OfType<string>()
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["ANY"];

            foreach (var method in methods)
            {
                routes.Add(new RouteAuthorization(
                    method.ToUpperInvariant(),
                    NormalizePattern(endpoint.RoutePattern),
                    authorizeData.Count > 0,
                    policies));
            }
        }

        return routes;
    }

    // A group's "/" route comes out with or without a trailing slash depending on how it was combined;
    // tests name routes without it.
    private static string NormalizePattern(RoutePattern pattern)
    {
        var text = pattern.RawText ?? string.Empty;
        return text.Length > 1 ? text.TrimEnd('/') : text;
    }
}
