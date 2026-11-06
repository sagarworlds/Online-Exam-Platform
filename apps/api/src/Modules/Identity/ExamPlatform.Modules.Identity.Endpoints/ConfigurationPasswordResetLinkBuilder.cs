using ExamPlatform.Modules.Identity.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>
/// Builds password-reset links from <c>Identity:LinkBaseUrl</c>, the address the web app is served from. It falls back to the first
/// allowed CORS origin, which in a normal deployment is that same address (so a deployment that already allows its website needs no
/// extra setting), and then to the local web app.
/// </summary>
/// <param name="configuration">The application configuration.</param>
internal sealed class ConfigurationPasswordResetLinkBuilder(IConfiguration configuration) : IPasswordResetLinkBuilder
{
    private const string DefaultBaseUrl = "http://localhost:4200";

    /// <inheritdoc />
    public string Build(string code)
    {
        var baseUrl = configuration["Identity:LinkBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DefaultBaseUrl;

        return $"{baseUrl.TrimEnd('/')}/password-reset/confirm#code={Uri.EscapeDataString(code)}";
    }
}
