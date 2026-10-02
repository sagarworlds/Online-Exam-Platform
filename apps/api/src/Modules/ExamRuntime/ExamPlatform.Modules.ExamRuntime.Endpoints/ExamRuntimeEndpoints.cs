using System.Security.Claims;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints;

/// <summary>Body of <c>PUT /v1/me/attempts/{attemptId}/answers/{questionId}</c>.</summary>
/// <param name="OptionId">The option the candidate chose.</param>
public sealed record SaveAnswerRequest(Guid OptionId);

/// <summary>Maps the ExamRuntime module's candidate-facing HTTP endpoints (FR-16 to FR-21).</summary>
public static class ExamRuntimeEndpoints
{
    /// <summary>Maps <c>/v1/me/exams</c> and the attempt routes.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapExamRuntimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Self-service: every route acts on the signed-in candidate's own data, taken from the token and
        // never from the request, so a signed-in caller is all that is asked of them. What they may take is
        // decided by their enrolment, not by a role; an exam or attempt that is not theirs answers 404.
        var me = endpoints.MapGroup("/v1/me").WithTags("ExamRuntime").RequireAuthorization();

        me.MapGet("/exams", ListMyExams)
            .Produces<IReadOnlyList<MyExamDto>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("ListMyExams")
            .WithDescription("List the published exams the signed-in candidate is enrolled in");

        me.MapPost("/exams/{examId:guid}/attempts", StartAttempt)
            .Produces<AttemptDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("StartAttempt")
            .WithDescription("Start the signed-in candidate's attempt at an exam, or resume the one they already have");

        me.MapGet("/attempts/{attemptId:guid}", GetAttempt)
            .Produces<AttemptDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetAttempt")
            .WithDescription("Read an attempt: its questions while open, its score once submitted");

        me.MapGet("/attempts/{attemptId:guid}/review", GetAttemptReview)
            .Produces<AttemptReviewDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("GetAttemptReview")
            .WithDescription("Read a submitted attempt with which answers were right, once the exam's author has released them");

        me.MapPut("/attempts/{attemptId:guid}/answers/{questionId:guid}", SaveAnswer)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("SaveAnswer")
            .WithDescription("Save the option chosen for one question of an open attempt");

        me.MapPost("/attempts/{attemptId:guid}/submit", SubmitAttempt)
            .Produces<AttemptDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("SubmitAttempt")
            .WithDescription("End an attempt and score it");
    }

    private static async Task<IResult> ListMyExams(ClaimsPrincipal user, MyExamsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(user.GetUserId(), ct));

    private static async Task<IResult> StartAttempt(Guid examId, ClaimsPrincipal user, StartAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, user.GetUserId(), ct));

    private static async Task<IResult> GetAttempt(Guid attemptId, ClaimsPrincipal user, GetAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(attemptId, user.GetUserId(), ct));

    private static async Task<IResult> GetAttemptReview(Guid attemptId, ClaimsPrincipal user, GetAttemptReviewHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(attemptId, user.GetUserId(), ct));

    private static async Task<IResult> SaveAnswer(
        Guid attemptId, Guid questionId, SaveAnswerRequest request, ClaimsPrincipal user, SaveAnswerHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(attemptId, user.GetUserId(), questionId, request.OptionId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SubmitAttempt(Guid attemptId, ClaimsPrincipal user, SubmitAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(attemptId, user.GetUserId(), ct));
}
