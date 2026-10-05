using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.Security;

namespace ExamPlatform.Api;

/// <summary><see cref="IRequestContext"/> read from the current HTTP request's access token.</summary>
public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    /// <inheritdoc />
    public Guid? UserId =>
        // Read tolerantly: an anonymous or odd token means "no actor", not an exception in the middle of an audit write.
        Guid.TryParse(User?.FindFirst(ClaimsPrincipalExtensions.SubjectClaim)?.Value, out var id) && id != Guid.Empty ? id : null;

    /// <inheritdoc />
    public string? Role => UserId is null ? null : User!.GetPrimaryRole();

    /// <inheritdoc />
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;

    private System.Security.Claims.ClaimsPrincipal? User =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;
}
