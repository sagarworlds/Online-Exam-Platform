using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
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
        CreateExamHandler handler,
        CancellationToken ct)
    {
        var command = new CreateExamCommand(
            request.SeriesId,
            request.Name,
            request.Description,
            request.CreatedBy);

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/exams/{result.Id}", result);
    }
}

/// <summary>Request DTO for creating an exam.</summary>
public record CreateExamRequest(
    Guid SeriesId,
    string Name,
    string? Description,
    Guid CreatedBy);
