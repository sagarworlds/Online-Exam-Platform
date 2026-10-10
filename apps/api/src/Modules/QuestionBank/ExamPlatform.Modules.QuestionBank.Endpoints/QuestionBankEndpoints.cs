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
        // Every route returns the answer key, so every route needs a permission, not just a signed-in caller. Changing questions needs
        // the authoring permission; reading them and commenting needs only the read permission, and approving or sending one back needs
        // the review permission, so a reviewer can do their part without being able to edit what they review (FR-8).
        var questions = endpoints.MapGroup("/v1/questions")
            .WithTags("QuestionBank")
            .RequireAuthorization(QuestionBankPermissions.Manage);
        var readers = endpoints.MapGroup("/v1/questions")
            .WithTags("QuestionBank")
            .RequireAuthorization(QuestionBankPermissions.Read);
        var reviewers = endpoints.MapGroup("/v1/questions")
            .WithTags("QuestionBank")
            .RequireAuthorization(QuestionBankPermissions.Review);

        questions.MapPost("/", CreateQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CreateQuestion")
            .WithDescription("Create a multiple-choice question with one correct option");

        readers.MapGet("/", ListQuestions)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListQuestions")
            .WithDescription("List the newest questions, 200 at a time (skip leaves out that many of the newest), optionally only those under a class, book or chapter, only unfiled ones, of one difficulty, on one topic, in one review status, or containing some text (q)");

        readers.MapPost("/duplicates", FindDuplicates)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("FindDuplicateQuestions")
            .WithDescription("List the questions already in the bank with the same wording as the one given, saying which also have the same options");

        questions.MapPost("/{questionId:guid}/translations", AddTranslation)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("AddQuestionTranslation")
            .WithDescription("Add a translation of a question in another language, linked to it: the translator gives the words, the answer key is copied");

        readers.MapGet("/{questionId:guid}/translations", ListTranslations)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListQuestionTranslations")
            .WithDescription("List a question and its linked translations, one per language");

        readers.MapGet("/{questionId:guid}/statistics", GetStatistics)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetQuestionStatistics")
            .WithDescription("The exams that hold a question, how many candidates answered it, how many were fully correct and how often each option was chosen");

        readers.MapGet("/topics", ListTopics)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListQuestionTopics")
            .WithDescription("List every topic in use, once each, alphabetically");

        readers.MapGet("/{questionId:guid}", GetQuestion)
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

        readers.MapGet("/{questionId:guid}/history", GetQuestionHistory)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetQuestionHistory")
            .WithDescription("List every version a question has had, oldest first");

        readers.MapGet("/{questionId:guid}/review-log", GetReviewLog)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetQuestionReviewLog")
            .WithDescription("A question's review thread, oldest first: comments and each step of the workflow");

        readers.MapPost("/{questionId:guid}/comments", CommentOnQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CommentOnQuestion")
            .WithDescription("Add a comment to a question's review thread, in any status");

        questions.MapPost("/{questionId:guid}/submit-for-review", SubmitForReview)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("SubmitQuestionForReview")
            .WithDescription("Put a draft question forward for review");

        reviewers.MapPost("/{questionId:guid}/approve", ApproveQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ApproveQuestion")
            .WithDescription("Approve a question that is in review");

        reviewers.MapPost("/{questionId:guid}/request-changes", RequestQuestionChanges)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("RequestQuestionChanges")
            .WithDescription("Send a question in review back to its author as a draft, with what has to change");

        questions.MapPost("/{questionId:guid}/retire", RetireQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("RetireQuestion")
            .WithDescription("Take a question out of use: it can no longer be added to an exam or drawn into a paper");

        questions.MapPost("/{questionId:guid}/restore", RestoreQuestion)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("RestoreQuestion")
            .WithDescription("Bring a retired question back as a draft");

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
            .WithDescription("Create questions from a CSV, Excel (base64) or JSON file; a bad row is reported and skipped, not the whole import");

        questions.MapGet("/export", ExportQuestions)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ExportQuestions")
            .WithDescription("Download questions matching the same filters as the list, as a CSV, Excel or JSON file ImportQuestions can read back");
    }

    private static async Task<IResult> FindDuplicates(FindDuplicatesRequest request, FindDuplicatesHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(request.Text, request.Options, request.ExcludeQuestionId, ct));

    private static async Task<IResult> AddTranslation(
        Guid questionId, AddTranslationRequest request, ClaimsPrincipal user, AddTranslationHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new AddTranslationCommand(questionId, request.Language, request.Text, request.Options, user.GetUserId(), request.AcceptedAnswers), ct);
        return Results.Created($"/v1/questions/{result.Id}", result);
    }

    private static async Task<IResult> ListTranslations(Guid questionId, ListTranslationsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(questionId, ct));

    private static async Task<IResult> GetStatistics(Guid questionId, GetQuestionStatisticsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(questionId, ct));

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
        return Results.Ok(await handler.HandleAsync(
            new EditQuestionCommand(questionId, request.Text, options, request.Difficulty, request.Topics, request.AllowsMultiple, request.IsTextAnswer, request.AcceptedAnswers, request.Explanation), ct));
    }

    private static async Task<IResult> CreateQuestion(
        CreateQuestionRequest request, ClaimsPrincipal user, CreateQuestionHandler handler, CancellationToken ct)
    {
        var options = request.Options?.Select(o => new NewQuestionOption(o?.Text, o?.IsCorrect ?? false, o?.IsPinned ?? false)).ToList();
        var result = await handler.HandleAsync(
            new CreateQuestionCommand(
                request.Text, options, user.GetUserId(), request.ChapterId, request.Difficulty, request.Topics, request.AllowsMultiple,
                request.AllowDuplicate, request.Language, request.IsTextAnswer, request.AcceptedAnswers, request.Explanation), ct);
        return Results.Created($"/v1/questions/{result.Id}", result);
    }

    private static async Task<IResult> ListQuestions(
        ListQuestionsHandler handler, Guid? bookId, Guid? chapterId, bool? unfiled, string? difficulty, string? topic, string? q, int? skip, string? status, string? language, Guid? classId, CancellationToken ct)
    {
        var statuses = QuestionStatusText.Parse(status) is { } one ? new[] { one } : null;
        return Results.Ok(await handler.HandleAsync(
            new QuestionFilter(bookId, chapterId, unfiled ?? false, QuestionDifficultyText.Parse(difficulty), Question.NormalizeTopic(topic), q, null, statuses,
                string.IsNullOrWhiteSpace(language) ? null : QuestionLanguage.Parse(language), classId), ct, skip ?? 0));
    }

    private static ReviewActor Actor(ClaimsPrincipal user) => new(user.GetUserId(), user.GetEmail());

    private static async Task<IResult> GetReviewLog(Guid questionId, GetQuestionReviewLogHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(questionId, ct));

    private static async Task<IResult> CommentOnQuestion(
        Guid questionId, ReviewCommentRequest request, ClaimsPrincipal user, QuestionReviewHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.CommentAsync(questionId, Actor(user), request.Comment, ct));

    private static async Task<IResult> SubmitForReview(
        Guid questionId, ReviewCommentRequest? request, ClaimsPrincipal user, QuestionReviewHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.SubmitAsync(questionId, Actor(user), request?.Comment, ct));

    private static async Task<IResult> ApproveQuestion(
        Guid questionId, ReviewCommentRequest? request, ClaimsPrincipal user, QuestionReviewHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.ApproveAsync(questionId, Actor(user), request?.Comment, ct));

    private static async Task<IResult> RequestQuestionChanges(
        Guid questionId, ReviewCommentRequest? request, ClaimsPrincipal user, QuestionReviewHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.RequestChangesAsync(questionId, Actor(user), request?.Comment, ct));

    private static async Task<IResult> RetireQuestion(
        Guid questionId, ReviewCommentRequest? request, ClaimsPrincipal user, QuestionReviewHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.RetireAsync(questionId, Actor(user), request?.Comment, ct));

    private static async Task<IResult> RestoreQuestion(
        Guid questionId, ReviewCommentRequest? request, ClaimsPrincipal user, QuestionReviewHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.RestoreAsync(questionId, Actor(user), request?.Comment, ct));

    private static async Task<IResult> ListTopics(ListTopicsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    private static async Task<IResult> GetQuestion(Guid questionId, GetQuestionHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(questionId, ct));

    private static async Task<IResult> CorrectAnswerKey(
        Guid questionId, CorrectAnswerKeyRequest request, ClaimsPrincipal user, CorrectAnswerKeyHandler handler, CancellationToken ct)
    {
        var command = new CorrectAnswerKeyCommand(
            questionId, request.CorrectOptionIds ?? [], request.Reason ?? string.Empty, user.GetUserId(), user.GetPrimaryRole(), request.AcceptedAnswers);
        return Results.Ok(await handler.HandleAsync(command, ct));
    }

    private static async Task<IResult> GetQuestionHistory(Guid questionId, GetQuestionHistoryHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(questionId, ct));

    private static async Task<IResult> ImportQuestions(
        ImportQuestionsRequest request, ClaimsPrincipal user, ImportQuestionsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(
            new ImportQuestionsCommand(request.Content ?? request.Csv ?? string.Empty, user.GetUserId(), QuestionFiles.ParseFormat(request.Format), request.AllowDuplicates), ct));

    private static async Task<IResult> ExportQuestions(
        ExportQuestionsHandler handler, HttpContext http, Guid? bookId, Guid? chapterId, bool? unfiled, string? difficulty, string? topic, string? q, string? format, Guid? classId, CancellationToken ct)
    {
        var filter = new QuestionFilter(bookId, chapterId, unfiled ?? false, QuestionDifficultyText.Parse(difficulty), Question.NormalizeTopic(topic), q, ClassId: classId);
        var file = await handler.ExportAsync(filter, QuestionFiles.ParseFormat(format), ct);
        // A workbook cannot hold a question whose text is longer than a cell, so the caller is told how many were left out.
        http.Response.Headers["X-Questions-Skipped"] = file.Skipped.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.File(file.Bytes, file.ContentType, file.FileName);
    }
}

/// <summary>Request body for creating a question. The author is the caller, so it is not part of the body.</summary>
/// <param name="Text">The question text; HTML from the author's editor, which the server sanitizes before storing it.</param>
/// <param name="Options">The answer options in display order; exactly one must be correct.</param>
/// <param name="ChapterId">The chapter to file the question under; omit or send null to leave it unfiled.</param>
/// <param name="Difficulty">"easy", "medium" or "hard"; omit or send null for unsaid.</param>
/// <param name="Topics">Up to five short topics such as "fractions"; omit for none.</param>
/// <param name="AllowsMultiple">True when more than one option is correct and a candidate must choose all of them; omitted means a single correct option.</param>
/// <param name="AllowDuplicate">True to add the question even when the bank already has the same wording and options; omitted means a repeat is refused.</param>
/// <param name="Language">"en", "hi" or "mr"; omitted means English. The same question in another language is added as a translation (FR-10).</param>
/// <param name="IsTextAnswer">True for a text question, where the candidate types the answer; omit it for a multiple-choice question.</param>
/// <param name="AcceptedAnswers">For a text question, the answers a typed answer may be (at least one); omit it for a multiple-choice question.</param>
/// <param name="Explanation">Why the correct answer is correct, as plain text, at most 2000 characters; omit it for none (FR-33).</param>
public sealed record CreateQuestionRequest(
    string? Text, IReadOnlyList<CreateQuestionOptionRequest?>? Options, Guid? ChapterId = null,
    string? Difficulty = null, IReadOnlyList<string?>? Topics = null, bool AllowsMultiple = false, bool AllowDuplicate = false,
    string? Language = null, bool IsTextAnswer = false, IReadOnlyList<string?>? AcceptedAnswers = null, string? Explanation = null);

/// <summary>Request body for adding a translation of a question (FR-10).</summary>
/// <param name="Language">The language of the translation, "en", "hi" or "mr"; required, and not one the question's group already has.</param>
/// <param name="Text">The translated question text; HTML from the author's editor, which the server sanitizes before storing it.</param>
/// <param name="Options">The translated option texts, one for each option of the question being translated, in the same order. Which is correct is copied, not sent.</param>
/// <param name="AcceptedAnswers">For a text question, the answers accepted in this language; omit it to use the question's own answers.</param>
public sealed record AddTranslationRequest(
    string? Language, string? Text, IReadOnlyList<string?>? Options, IReadOnlyList<string?>? AcceptedAnswers = null);

/// <summary>Asks which questions already repeat one that is about to be added (FR-9).</summary>
/// <param name="Text">The question text as the editor produced it (HTML).</param>
/// <param name="Options">The option texts.</param>
/// <param name="ExcludeQuestionId">A question being edited, which is not a duplicate of itself.</param>
public sealed record FindDuplicatesRequest(string? Text, IReadOnlyList<string?>? Options, Guid? ExcludeQuestionId = null);

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
/// <param name="IsTextAnswer">True for a text question; omit it for a multiple-choice one. Like the answer key it is locked once candidates have answered.</param>
/// <param name="AcceptedAnswers">For a text question, the accepted answers after the edit (at least one); omit it for a multiple-choice one.</param>
/// <param name="Explanation">The explanation after the edit, as plain text; omitting it clears the explanation, as the body is the whole new content (FR-33).</param>
public sealed record EditQuestionRequest(
    string? Text, IReadOnlyList<EditQuestionOptionRequest?>? Options, string? Difficulty = null, IReadOnlyList<string?>? Topics = null,
    bool AllowsMultiple = false, bool IsTextAnswer = false, IReadOnlyList<string?>? AcceptedAnswers = null, string? Explanation = null);

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
/// <param name="AcceptedAnswers">For a text question, the answers that should be accepted, replacing the current list; leave it out for a multiple-choice question.</param>
public sealed record CorrectAnswerKeyRequest(
    IReadOnlyCollection<Guid>? CorrectOptionIds, string? Reason, IReadOnlyList<string?>? AcceptedAnswers = null);

/// <summary>Request body for a review step or comment (FR-8).</summary>
/// <param name="Comment">The comment: required to send a question back or to comment, optional for the other steps.</param>
public sealed record ReviewCommentRequest(string? Comment = null);

/// <summary>Request body for importing questions from a file.</summary>
/// <param name="Content">The file's contents: text for CSV and JSON, base64 for Excel. A table's header row is included.</param>
/// <param name="Format">"csv" (the default), "xlsx" or "json".</param>
/// <param name="Csv">The original name for <paramref name="Content"/> when the file is CSV; still accepted.</param>
/// <param name="AllowDuplicates">Create rows even when the same question is already in the bank or earlier in the file; omitted means repeats are left out.</param>
public sealed record ImportQuestionsRequest(string? Content = null, string? Format = null, string? Csv = null, bool AllowDuplicates = false);
