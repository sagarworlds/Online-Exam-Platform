using System.Security.Claims;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.ExamAuthoring.Endpoints;

/// <summary>Maps the ExamAuthoring module's HTTP endpoints (FR-11, FR-12, FR-13).</summary>
public static class ExamAuthoringEndpoints
{
    /// <summary>Maps <c>POST /v1/exams</c> and other exam authoring endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapExamAuthoringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The group only demands a signed-in caller. Being signed in says nothing about being allowed to
        // author exams (a candidate is signed in too), so every route also names the permission it needs (FR-2, NFR-5).
        var exams = endpoints.MapGroup("/v1/exams")
            .WithTags("ExamAuthoring")
            .RequireAuthorization();

        exams.MapPost("/", CreateExam)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CreateExam")
            .WithDescription("Create a new exam");
    }

    private static async Task<IResult> CreateExam(
        CreateExamRequest request,
        ClaimsPrincipal user,
        CreateExamHandler handler,
        CancellationToken ct)
    {
        // The creator is the authenticated caller, never a value from the body: a client-supplied
        // id would let any signed-in user author an exam in someone else's name (FR-2, NFR-5).
        var command = new CreateExamCommand(
            request.SeriesId,
            request.Name,
            request.Description,
            user.GetUserId());

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/exams/{result.Id}", result);
    }
}

/// <summary>Request DTO for creating an exam. The creator is the caller, so it is not part of the body.</summary>
/// <param name="SeriesId">The exam series the exam belongs to; omit or send null for a standalone exam.</param>
/// <param name="Name">Display name of the exam.</param>
/// <param name="Description">Optional longer description shown to candidates.</param>
public record CreateExamRequest(
    Guid? SeriesId,
    string Name,
    string? Description);
