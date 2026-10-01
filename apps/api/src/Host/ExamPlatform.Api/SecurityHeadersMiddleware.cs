namespace ExamPlatform.Api;

/// <summary>
/// Adds defensive response headers to every API response (NFR-5). The API only returns
/// JSON and problem details, never a page, so the policy can be as strict as possible:
/// nothing may sniff a content type, frame a response, load a sub-resource from one, or
/// learn from a <c>Referer</c> where the caller came from.
/// </summary>
/// <remarks>
/// The browsable API reference (<see cref="ApiReferencePathPrefix"/>) is the one exception:
/// it is an HTML page that loads its own script and styles, which a
/// <c>default-src 'none'</c> policy would blank out. It only exists in Development, so the
/// middleware is told whether it is mapped and exempts that path from the
/// <c>Content-Security-Policy</c> only; the other headers still apply to it.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="apiReferenceEnabled">Whether the API reference UI is mapped in this environment.</param>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, bool apiReferenceEnabled)
{
    /// <summary>Where the API reference UI is served when it is enabled.</summary>
    public const string ApiReferencePathPrefix = "/scalar";

    /// <summary>
    /// Allows nothing to load and nothing to embed the response, which is everything a
    /// JSON API needs and no more.
    /// </summary>
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>Invokes the middleware.</summary>
    /// <param name="context">The current request.</param>
    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting, not a direct write: the exception handler clears the response headers
        // before it writes an error, which would otherwise drop these from every 500.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            if (!(apiReferenceEnabled && context.Request.Path.StartsWithSegments(ApiReferencePathPrefix)))
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
