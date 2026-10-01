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
        var exams = endpoints.MapGroup("/v1/exams").WithTags("ExamAuthoring").RequireAuthorization();

        exams.MapPost("/", CreateExam)
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
