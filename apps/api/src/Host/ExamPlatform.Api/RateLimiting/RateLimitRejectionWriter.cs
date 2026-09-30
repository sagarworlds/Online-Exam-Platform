using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamPlatform.Api.RateLimiting;

/// <summary>
/// Writes the response for a request rejected by any rate limiter — the global limiter
/// and every named policy a module adds — as the same <see cref="ProblemDetails"/> shape
/// <see cref="DomainExceptionHandler"/> uses for typed errors, so a client handles a 429
/// like any other structured error instead of an empty body.
/// </summary>
public static class RateLimitRejectionWriter
{
    /// <summary>The stable error code sent as the problem's title.</summary>
    public const string ErrorCode = "rate_limited";

    /// <summary>
    /// Sets <c>Retry-After</c> when the limiter reports one and writes a 429 problem body.
    /// Signature matches <see cref="RateLimiterOptions.OnRejected"/>.
    /// </summary>
    /// <param name="context">The rejected request and the limiter's failed lease.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteProblemDetailsAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        // Not logged here: the rate-limiting middleware already logs each rejection at
        // Debug, and a Warning per rejected request would let a flooding client flood the
        // logs too.
        var httpContext = context.HttpContext;
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            // Whole seconds, rounded up: Retry-After is an integer, and rounding down
            // would invite a retry that is still inside the window and gets rejected again.
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = ErrorCode,
                Detail = "Too many requests. Wait before trying again; the Retry-After header, when present, says how many seconds.",
                Instance = httpContext.Request.Path,
            },
            cancellationToken);
    }
}
