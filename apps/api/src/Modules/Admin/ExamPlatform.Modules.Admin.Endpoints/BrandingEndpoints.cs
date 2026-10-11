using ExamPlatform.Modules.Admin.Application.Commands;
using ExamPlatform.Modules.Admin.Application.Queries;
using ExamPlatform.Modules.Admin.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Admin.Endpoints;

/// <summary>
/// Maps the institute branding routes (FR-41). Reading the branding and its logo is open, because the candidate pages, including sign-in,
/// show it before anyone is signed in. Changing it needs <c>exam.manage</c>, the permission the exam editor uses, as FR-41 asks.
/// </summary>
public static class BrandingEndpoints
{
    private const string ManagePolicy = "permission:exam.manage";

    /// <summary>Maps <c>/v1/branding</c> and its <c>/logo</c> route.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapBrandingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var branding = endpoints.MapGroup("/v1/branding").WithTags("Admin");

        branding.MapGet("/", GetBranding)
            .AllowAnonymous()
            .WithName("GetBranding")
            .WithDescription("The institute's name and primary colour for the candidate pages; the default look when none is set");

        branding.MapGet("/logo", GetLogo)
            .AllowAnonymous()
            .WithName("GetBrandingLogo")
            .WithDescription("The institute's logo, as a PNG, JPEG or WebP image");

        branding.MapPut("/", UpdateBranding)
            .RequireAuthorization(ManagePolicy)
            .WithName("UpdateBranding")
            .WithDescription("Set the institute's name and primary colour");

        branding.MapPut("/logo", SetLogo)
            .RequireAuthorization(ManagePolicy)
            .WithName("SetBrandingLogo")
            .WithDescription("Replace the institute's logo: the raw image bytes, at most 256 KB");

        branding.MapDelete("/logo", RemoveLogo)
            .RequireAuthorization(ManagePolicy)
            .WithName("RemoveBrandingLogo")
            .WithDescription("Remove the institute's logo");
    }

    private static async Task<IResult> GetBranding(GetBrandingHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    private static async Task<IResult> GetLogo(HttpContext httpContext, GetBrandingLogoHandler handler, CancellationToken ct)
    {
        var logo = await handler.HandleAsync(ct);

        // The time of the last change is the tag, so a replaced logo gets a new one and a browser never shows a stale image. no-cache
        // makes the browser check the tag each time, and a match is answered with 304 rather than the bytes again.
        var entityTag = $"\"{logo.UpdatedAtUtc.Ticks:x}\"";
        httpContext.Response.Headers.ETag = entityTag;
        httpContext.Response.Headers.CacheControl = "no-cache";
        if (httpContext.Request.Headers.IfNoneMatch.ToString() == entityTag)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(logo.Content, logo.ContentType);
    }

    private static async Task<IResult> UpdateBranding(BrandingRequest request, UpdateBrandingHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new UpdateBrandingCommand(request.InstituteName, request.PrimaryColour), ct));

    private static async Task<IResult> SetLogo(HttpRequest request, SetBrandingLogoHandler handler, CancellationToken ct)
    {
        // One byte past the limit is read: enough to tell "exactly at the limit" from "over it", without buffering an arbitrarily large upload.
        var content = await ReadUpToAsync(request.Body, LogoImage.MaxBytes + 1, ct);
        return Results.Ok(await handler.HandleAsync(new SetBrandingLogoCommand(content), ct));
    }

    private static async Task<IResult> RemoveLogo(RemoveBrandingLogoHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    /// <summary>Reads a request body, but no further than <paramref name="limit"/> bytes.</summary>
    /// <param name="body">The request body.</param>
    /// <param name="limit">The most bytes to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bytes read: all of the body when it is shorter than the limit.</returns>
    private static async Task<byte[]> ReadUpToAsync(Stream body, int limit, CancellationToken ct)
    {
        var buffer = new byte[limit];
        var total = 0;
        while (total < limit)
        {
            var read = await body.ReadAsync(buffer.AsMemory(total, limit - total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return buffer[..total];
    }
}

/// <summary>Request body for setting the institute's name and primary colour.</summary>
/// <param name="InstituteName">The name candidates should see; blank or omitted clears it.</param>
/// <param name="PrimaryColour">A six-digit hex colour such as <c>#1A56DB</c>; blank or omitted clears it.</param>
public sealed record BrandingRequest(string? InstituteName, string? PrimaryColour);
