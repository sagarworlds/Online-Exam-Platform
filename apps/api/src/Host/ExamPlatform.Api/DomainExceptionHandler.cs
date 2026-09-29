using ExamPlatform.SharedKernel.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ExamPlatform.Api;

/// <summary>
/// Maps any <see cref="DomainException"/> raised by a handler to a structured
/// <see cref="ProblemDetails"/> response, using the exception's own declared
/// <see cref="DomainException.HttpStatusCode"/> and <see cref="DomainException.ErrorCode"/> —
/// so a new module's typed errors are handled correctly without editing this class
/// (Open/Closed Principle). Anything that is not a <see cref="DomainException"/> is
/// left to the default developer-exception-page/problem-details middleware.
/// </summary>
public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        logger.LogWarning(
            domainException,
            "Request failed with domain error {ErrorCode} ({StatusCode})",
            domainException.ErrorCode,
            domainException.HttpStatusCode);

        httpContext.Response.StatusCode = domainException.HttpStatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = domainException.HttpStatusCode,
                Title = domainException.ErrorCode,
                Detail = domainException.Message,
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
