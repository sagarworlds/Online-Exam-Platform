using ExamPlatform.Modules.Consent.Contracts;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Consent.Endpoints;

/// <summary>Maps the Consent module's HTTP endpoints (FR-44), exactly the three routes named in exam-platform-requirements.md section 13.</summary>
public static class ConsentEndpoints
{
    /// <summary>Maps <c>GET/POST/DELETE /v1/consent*</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapConsentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/consent").WithTags("Consent").RequireAuthorization();

        group.MapGet("/status", async (Guid subjectId, ConsentPurpose purpose, IConsentService service, CancellationToken ct) =>
        {
            var status = await service.GetStatusAsync(subjectId, purpose, ct);
            return Results.Ok(status);
        });

        group.MapPost("/", async (RecordConsentHttpRequest request, HttpContext http, IConsentService service, CancellationToken ct) =>
        {
            var record = await service.RecordConsentAsync(
                new RecordConsentRequest(request.SubjectId, request.Purpose, request.NoticeVersionId, http.User.GetUserId()),
                ct);
            return Results.Ok(record);
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, IConsentService service, CancellationToken ct) =>
        {
            await service.WithdrawConsentAsync(id, http.User.GetUserId(), ct);
            return Results.NoContent();
        });
    }
}

/// <summary>Request body for <c>POST /v1/consent</c> — <c>GivenById</c> is taken from the caller's token, not the body.</summary>
public sealed record RecordConsentHttpRequest(Guid SubjectId, ConsentPurpose Purpose, Guid NoticeVersionId);
