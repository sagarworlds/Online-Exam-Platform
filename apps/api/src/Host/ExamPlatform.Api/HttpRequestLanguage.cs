using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Api;

/// <summary>Reads the languages the caller asked for from the current request's <c>Accept-Language</c> header (FR-51).</summary>
public sealed class HttpRequestLanguage(IHttpContextAccessor accessor) : IRequestLanguage
{
    /// <inheritdoc />
    public IReadOnlyList<string> Preferred =>
        RequestLanguage.Parse(accessor.HttpContext?.Request.Headers.AcceptLanguage.ToString());
}
