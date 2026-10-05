using System.Security.Claims;
using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
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
            .WithDescription("List the newest questions, 200 at a time (skip leaves out that many of the newest), optionally only those under a book or chapter, only unfiled ones, of one difficulty, on one topic, or containing some text (q)");

        questions.MapGet("/topics", ListTopics)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListQuestionTopics")
            .WithDescription("List every topic in use, once each, alphabetically");

        questions.MapGet("/{questionId:guid}", GetQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetQuestion")
            .WithDescription("Get one question with its answer key");

        questions.MapPut("/{questionId:guid}", EditQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("EditQuestion")
            .WithDescription("Edit a question; once candidates have answered it only the wording can change");

        questions.MapDelete("/{questionId:guid}", DeleteQuestion)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("DeleteQuestion")
            .WithDescription("Delete a question; refused while an exam holds it or candidates have answered it");

        questions.MapPost("/{questionId:guid}/correct-answer-key", CorrectAnswerKey)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CorrectAnswerKey")
            .WithDescription("Correct which options are right, even after candidates have answered; rescores every attempt it affects");

        // A collection action, not /{questionId}/chapter, so filing one question and filing a hundred are the same call.
        questions.MapPost("/placement", FileQuestions)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("FileQuestions")
            .WithDescription("File one or more questions under a chapter, all or none");

        questions.MapPost("/import", ImportQuestions)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ImportQuestions")
            .WithDescription("Create questions from a CSV file; a bad row is reported and skipped, not the whole import");

        questions.MapGet("/export", ExportQuestions)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ExportQuestions")
            .WithDescription("Download questions matching the same filters as the list, as a CSV file ImportQuestions can read back");
    }

    private static async Task<IResult> FileQuestions(FileQuestionsRequest request, FileQuestionsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new FileQuestionsCommand(request.QuestionIds, request.ChapterId), ct));

    private static async Task<IResult> DeleteQuestion(Guid questionId, DeleteQuestionHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(questionId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> EditQuestion(
        Guid questionId, EditQuestionRequest request, EditQuestionHandler handler, CancellationToken ct)
    {
        var options = request.Options?.Select(o => new QuestionOptionEdit(o?.Id, o?.Text, o?.IsCorrect ?? false, o?.IsPinned ?? false)).ToList();
        return Results.Ok(await handler.HandleAsync(new EditQuestionCommand(questionId, request.Text, options, request.Difficulty, request.Topics, request.AllowsMultiple), ct));
    }

    private static async Task<IResult> CreateQuestion(
        CreateQuestionRequest request, ClaimsPrincipal user, CreateQuestionHandler handler, CancellationToken ct)
    {
        var options = request.Options?.Select(o => new NewQuestionOption(o?.Text, o?.IsCorrect ?? false, o?.IsPinned ?? false)).ToList();
        var result = await handler.HandleAsync(new CreateQuestionCommand(request.Text, options, user.GetUserId(), request.ChapterId, request.Difficulty, request.Topics, request.AllowsMultiple), ct);
        return Results.Created($"/v1/questions/{result.Id}", result);
    }

    private static async Task<IResult> ListQuestions(
        ListQuestionsHandler handler, Guid? bookId, Guid? chapterId, bool? unfiled, string? difficulty, string? topic, string? q, int? skip, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(
            new QuestionFilter(bookId, chapterId, unfiled ?? false, QuestionDifficultyText.Parse(difficulty), Question.NormalizeTopic(topic), q), ct, skip ?? 0));

    private static async Task<IResult> ListTopics(ListTopicsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    private static async Task<IResult> GetQuestion(Guid questionId, GetQuestionHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(questionId, ct));

    private static async Task<IResult> CorrectAnswerKey(
        Guid questionId, CorrectAnswerKeyRequest request, ClaimsPrincipal user, CorrectAnswerKeyHandler handler, CancellationToken ct)
    {
        var command = new CorrectAnswerKeyCommand(
            questionId, request.CorrectOptionIds ?? [], request.Reason ?? string.Empty, user.GetUserId(), user.GetPrimaryRole());
        return Results.Ok(await handler.HandleAsync(command, ct));
    }

    private static async Task<IResult> ImportQuestions(
        ImportQuestionsRequest request, ClaimsPrincipal user, ImportQuestionsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new ImportQuestionsCommand(request.Csv ?? string.Empty, user.GetUserId()), ct));

    private static async Task<IResult> ExportQuestions(
        ExportQuestionsHandler handler, Guid? bookId, Guid? chapterId, bool? unfiled, string? difficulty, string? topic, string? q, CancellationToken ct)
    {
        var filter = new QuestionFilter(bookId, chapterId, unfiled ?? false, QuestionDifficultyText.Parse(difficulty), Question.NormalizeTopic(topic), q);
        var csv = await handler.HandleAsync(filter, ct);
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "questions.csv");
    }
}

/// <summary>Request body for creating a question. The author is the caller, so it is not part of the body.</summary>
/// <param name="Text">The question text; HTML from the author's editor, which the server sanitizes before storing it.</param>
/// <param name="Options">The answer options in display order; exactly one must be correct.</param>
/// <param name="ChapterId">The chapter to file the question under; omit or send null to leave it unfiled.</param>
/// <param name="Difficulty">"easy", "medium" or "hard"; omit or send null for unsaid.</param>
/// <param name="Topics">Up to five short topics such as "fractions"; omit for none.</param>
/// <param name="AllowsMultiple">True when more than one option is correct and a candidate must choose all of them; omitted means a single correct option.</param>
public sealed record CreateQuestionRequest(
    string? Text, IReadOnlyList<CreateQuestionOptionRequest?>? Options, Guid? ChapterId = null,
    string? Difficulty = null, IReadOnlyList<string?>? Topics = null, bool AllowsMultiple = false);

/// <summary>Request body for filing questions under a chapter.</summary>
/// <param name="QuestionIds">The questions to file, at least one.</param>
/// <param name="ChapterId">The chapter to file them under; it must be open.</param>
public sealed record FileQuestionsRequest(IReadOnlyList<Guid>? QuestionIds, Guid ChapterId);

/// <summary>Request body for editing a question: its whole new content, not a patch.</summary>
/// <param name="Text">The question text; HTML from the author's editor, which the server sanitizes before storing it.</param>
/// <param name="Options">All the options after the edit, in display order; an option the question already has is named by its id.</param>
/// <param name="Difficulty">"easy", "medium" or "hard"; omitting it clears the difficulty, as the body is the whole new content.</param>
/// <param name="Topics">The topics after the edit; omitting them clears the topics. Allowed even once candidates have answered.</param>
/// <param name="AllowsMultiple">Whether more than one option is correct; omitting it means a single correct option. Locked once candidates have answered.</param>
public sealed record EditQuestionRequest(
    string? Text, IReadOnlyList<EditQuestionOptionRequest?>? Options, string? Difficulty = null, IReadOnlyList<string?>? Topics = null,
    bool AllowsMultiple = false);

/// <summary>One option in an <see cref="EditQuestionRequest"/>.</summary>
/// <param name="Id">The id of the existing option being edited; omit it for a new option.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer.</param>
/// <param name="IsPinned">Whether the option keeps its place when options are shuffled; omitted means not pinned.</param>
public sealed record EditQuestionOptionRequest(Guid? Id, string? Text, bool IsCorrect, bool IsPinned = false);

/// <summary>One option in a <see cref="CreateQuestionRequest"/>.</summary>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer.</param>
/// <param name="IsPinned">Whether the option keeps its place when options are shuffled; omitted means not pinned.</param>
public sealed record CreateQuestionOptionRequest(string? Text, bool IsCorrect, bool IsPinned = false);

/// <summary>Request body for correcting a question's answer key.</summary>
/// <param name="CorrectOptionIds">The ids of the options that are actually correct, replacing the current key.</param>
/// <param name="Reason">Why the key is being corrected; shown to a candidate whose score moves because of it.</param>
public sealed record CorrectAnswerKeyRequest(IReadOnlyCollection<Guid>? CorrectOptionIds, string? Reason);

/// <summary>Request body for importing questions from a CSV file.</summary>
/// <param name="Csv">The file's contents, header row included.</param>
public sealed record ImportQuestionsRequest(string? Csv);
