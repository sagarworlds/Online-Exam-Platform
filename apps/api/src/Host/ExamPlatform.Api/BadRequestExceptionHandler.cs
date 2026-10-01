using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ExamPlatform.Api;

/// <summary>
/// Maps a request the framework could not bind (a body that is not valid JSON, or a field of
/// the wrong type such as a garbled date of birth) to a 400 <see cref="ProblemDetails"/> in the
/// same shape <see cref="DomainExceptionHandler"/> uses for typed errors. Without it the
/// exception reaches the default handler and a client's malformed input is reported as a 500.
/// </summary>
public sealed class BadRequestExceptionHandler(ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>The error code a client sees for a request body the API could not read.</summary>
    public const string ErrorCode = "invalid_request";

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        logger.LogWarning(badRequest, "Request could not be read ({StatusCode})", badRequest.StatusCode);

        httpContext.Response.StatusCode = badRequest.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = ErrorCode,
                // Fixed text: the framework's own message names internal types and parameters.
                Detail = "The request could not be read. Check that it is well-formed and that each field has the expected type.",
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
