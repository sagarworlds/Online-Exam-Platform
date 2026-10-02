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

/// <summary>Body of <c>POST /v1/exams/{examId}/candidates/{candidateId}/extra-attempts</c>.</summary>
/// <param name="Reason">Why the candidate is being given another attempt; optional, at most 500 characters.</param>
public sealed record GrantExtraAttemptRequest(string? Reason);

/// <summary>Maps the ExamRuntime module's HTTP endpoints: the candidate's own (FR-16 to FR-21) and the staff's view of attempts.</summary>
public static class ExamRuntimeEndpoints
{
    /// <summary>Maps <c>/v1/me/exams</c>, the attempt routes, and the staff routes that list an exam's attempts and grant extra ones.</summary>
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

        // Staff routes. Unlike the candidate's own, these act on someone else's data, so each names the permission it needs and
        // the candidate is taken from the route, while the administrator who grants is taken from their token.
        var exams = endpoints.MapGroup("/v1/exams").WithTags("ExamRuntime").RequireAuthorization();

        exams.MapGet("/{examId:guid}/attempts", ListExamAttempts)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<ExamAttemptsDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ListExamAttempts")
            .WithDescription("List an exam's enrolled candidates with their attempts and whether another can be granted");

        exams.MapPost("/{examId:guid}/candidates/{candidateId:guid}/extra-attempts", GrantExtraAttempt)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<ExamCandidateDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("GrantExtraAttempt")
            .WithDescription("Give one enrolled candidate one more attempt at an exam, once they have used the ones they hold");
    }

    private static async Task<IResult> ListExamAttempts(Guid examId, ListExamAttemptsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, ct));

    private static async Task<IResult> GrantExtraAttempt(
        Guid examId, Guid candidateId, GrantExtraAttemptRequest? request, ClaimsPrincipal user, GrantExtraAttemptHandler handler, CancellationToken ct)
    {
        var row = await handler.HandleAsync(examId, candidateId, user.GetUserId(), request?.Reason, ct);
        return Results.Created($"/v1/exams/{examId}/attempts", row);
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
