using System.Security.Claims;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.QuestionBank.Endpoints;

/// <summary>Maps the QuestionBank module's HTTP endpoints (FR-5).</summary>
public static class QuestionBankEndpoints
{
    /// <summary>Maps <c>/v1/questions</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapQuestionBankEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Every route returns the answer key, so every route needs the authoring permission, not just a signed-in caller.
        var questions = endpoints.MapGroup("/v1/questions")
            .WithTags("QuestionBank")
            .RequireAuthorization(QuestionBankPermissions.Manage);

        questions.MapPost("/", CreateQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CreateQuestion")
            .WithDescription("Create a multiple-choice question with one correct option");

        questions.MapGet("/", ListQuestions)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListQuestions")
            .WithDescription("List the newest questions");

        questions.MapGet("/{questionId:guid}", GetQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetQuestion")
            .WithDescription("Get one question with its answer key");
    }

    private static async Task<IResult> CreateQuestion(
        CreateQuestionRequest request, ClaimsPrincipal user, CreateQuestionHandler handler, CancellationToken ct)
    {
        var options = request.Options?.Select(o => new NewQuestionOption(o?.Text, o?.IsCorrect ?? false)).ToList();
        var result = await handler.HandleAsync(new CreateQuestionCommand(request.Text, options, user.GetUserId()), ct);
        return Results.Created($"/v1/questions/{result.Id}", result);
    }

    private static async Task<IResult> ListQuestions(ListQuestionsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    private static async Task<IResult> GetQuestion(Guid questionId, GetQuestionHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(questionId, ct));
}

/// <summary>Request body for creating a question. The author is the caller, so it is not part of the body.</summary>
/// <param name="Text">The question text.</param>
/// <param name="Options">The answer options in display order; exactly one must be correct.</param>
public sealed record CreateQuestionRequest(string? Text, IReadOnlyList<CreateQuestionOptionRequest?>? Options);

/// <summary>One option in a <see cref="CreateQuestionRequest"/>.</summary>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer.</param>
public sealed record CreateQuestionOptionRequest(string? Text, bool IsCorrect);
