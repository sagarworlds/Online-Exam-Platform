using ExamPlatform.Modules.Proctoring.Application.Commands;
using ExamPlatform.Modules.Proctoring.Application.Dtos;
using ExamPlatform.Modules.Proctoring.Application.Queries;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace ExamPlatform.Modules.Proctoring.Endpoints;

/// <summary>Maps the Proctoring module's HTTP endpoints: running the risk score and the human review of flagged attempts (FR-27).</summary>
public static class ProctoringEndpoints
{
    /// <summary>
    /// Maps <c>/v1/proctoring</c>. Every route is for staff with the review permission: the score is never shown to a candidate, and
    /// nothing on these routes changes a candidate's attempt.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapProctoringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var proctoring = endpoints.MapGroup("/v1/proctoring")
            .WithTags("Proctoring")
            .RequireAuthorization();

        proctoring.MapGet("/exams/{examId:guid}/risk-flags", ListRiskFlags)
            .RequireAuthorization(ProctoringPermissions.Review)
            .Produces<RiskFlagQueueDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ListRiskFlags")
            .WithDescription("List one page of an exam's review queue, highest score first, with the signals behind each score");

        proctoring.MapPost("/exams/{examId:guid}/risk-scan", RunRiskScan)
            .RequireAuthorization(ProctoringPermissions.Review)
            .Produces<RiskScanResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("RunRiskScan")
            .WithDescription("Score every finished attempt at an exam and flag those at or above the threshold; decided flags are left as they are");

        proctoring.MapPost("/risk-flags/{assessmentId:guid}/review", ReviewRiskFlag)
            .RequireAuthorization(ProctoringPermissions.Review)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("ReviewRiskFlag")
            .WithDescription("Mark a flagged attempt reviewed; an optional note is kept with the decision");

        proctoring.MapPost("/risk-flags/{assessmentId:guid}/dismiss", DismissRiskFlag)
            .RequireAuthorization(ProctoringPermissions.Review)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("DismissRiskFlag")
            .WithDescription("Dismiss a flag; the note saying why is required");
    }

    /// <summary>The note a reviewer writes with a decision.</summary>
    /// <param name="Note">The note; required for a dismissal, optional for a review.</param>
    public sealed record RiskFlagDecisionRequest(string? Note);

    private static async Task<IResult> ListRiskFlags(
        Guid examId,
        string? filter,
        int? page,
        int? pageSize,
        ListRiskFlagsHandler handler,
        CancellationToken ct)
    {
        var parsedFilter = RiskFlagFilterText.Parse(filter);
        var result = await handler.HandleAsync(examId, parsedFilter, PageRequest.Create(page, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RunRiskScan(
        Guid examId, RunRiskScanHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, ct));

    private static async Task<IResult> ReviewRiskFlag(
        Guid assessmentId,
        RiskFlagDecisionRequest? request,
        ClaimsPrincipal user,
        ReviewRiskFlagHandler handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(assessmentId, user.GetUserId(), request?.Note, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DismissRiskFlag(
        Guid assessmentId,
        RiskFlagDecisionRequest? request,
        ClaimsPrincipal user,
        DismissRiskFlagHandler handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(assessmentId, user.GetUserId(), request?.Note, ct);
        return Results.NoContent();
    }
}
