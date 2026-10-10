using ExamPlatform.Modules.Guardian.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace ExamPlatform.Modules.Guardian.Endpoints;

/// <summary>
/// Builds the guardian's confirmation link from <c>Guardian:LinkBaseUrl</c>, the address the web app is served from. It falls back to
/// the first allowed CORS origin, which in a normal deployment is that same address, and then to the local web app.
/// </summary>
/// <param name="configuration">The application configuration.</param>
internal sealed class ConfigurationGuardianConsentLinkBuilder(IConfiguration configuration) : IGuardianConsentLinkBuilder
{
    private const string DefaultBaseUrl = "http://localhost:4200";

    /// <inheritdoc />
    public string Build(string rawToken)
    {
        var baseUrl = configuration["Guardian:LinkBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DefaultBaseUrl;

        return $"{baseUrl.TrimEnd('/')}/guardian/confirm-link?token={Uri.EscapeDataString(rawToken)}";
    }
}
