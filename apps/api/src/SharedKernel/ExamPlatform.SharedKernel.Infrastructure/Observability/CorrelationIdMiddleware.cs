using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.SharedKernel.Infrastructure.Observability;

/// <summary>
/// Gives every request one correlation id (see <see cref="CorrelationId"/>): the caller's own when it sent an acceptable one,
/// a new one otherwise. The id becomes the request's <see cref="HttpContext.TraceIdentifier"/>, so the audit trail, the problem
/// details' <c>traceId</c> and the request log all carry the same value without any of them minting a second one. It is
/// written to the response header and opens a log scope, so every line the request writes names it.
/// </summary>
/// <remarks>
/// The header is set in <c>OnStarting</c>, not directly: the exception handler clears response headers before it writes an
/// error, and a 500 would otherwise lose the id a caller needs to quote.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="logger">Opens the log scope that names the id on every line of the request.</param>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>The key the log scope uses for the id; the JSON formatter reads it back into its own field.</summary>
    public const string ScopeKey = RedactingJsonConsoleFormatter.CorrelationIdKey;

    /// <summary>Runs the rest of the pipeline with the request's correlation id in place.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the rest of the pipeline has.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var id = ResolveId(context.Request.Headers[CorrelationId.HeaderName].ToString());
        context.TraceIdentifier = id;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { [ScopeKey] = id }))
        {
            await next(context);
        }
    }

    /// <summary>Keeps the caller's id when it is acceptable, and otherwise makes a new one.</summary>
    /// <param name="incoming">The value of the caller's header, or an empty string when it sent none.</param>
    /// <returns>The id this request will use.</returns>
    public static string ResolveId(string incoming) =>
        CorrelationId.IsAcceptable(incoming) ? incoming : CorrelationId.NewId();
}
