using ExamPlatform.Modules.Invite.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace ExamPlatform.Modules.Invite.Endpoints;

/// <summary>
/// Builds invitation links from <c>Invite:LinkBaseUrl</c>, the address the web app is served from. It falls back to
/// the first allowed CORS origin, which in a normal deployment is that same address.
/// </summary>
/// <param name="configuration">The application configuration.</param>
internal sealed class ConfigurationInviteLinkBuilder(IConfiguration configuration) : IInviteLinkBuilder
{
    private const string DefaultBaseUrl = "http://localhost:4200";

    /// <inheritdoc />
    public string Build(string code) => $"{BaseUrl()}/invite?code={Uri.EscapeDataString(code)}";

    // In the fragment, not the query: this code signs someone in, and a browser never sends a fragment to a server, so it stays out of
    // access logs and Referer headers. The web page reads it and takes it off the address.
    /// <inheritdoc />
    public string BuildDirect(string code) => $"{BaseUrl()}/invite/direct#code={Uri.EscapeDataString(code)}";

    private string BaseUrl()
    {
        var baseUrl = configuration["Invite:LinkBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DefaultBaseUrl;

        return baseUrl.TrimEnd('/');
    }
}
