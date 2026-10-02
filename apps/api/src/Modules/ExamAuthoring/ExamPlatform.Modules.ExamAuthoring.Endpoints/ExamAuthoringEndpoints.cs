using System.Security.Claims;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Queries;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.ExamAuthoring.Endpoints;

/// <summary>Maps the ExamAuthoring module's HTTP endpoints (FR-11, FR-12, FR-13).</summary>
public static class ExamAuthoringEndpoints
{
    /// <summary>Maps <c>/v1/exams</c> and its sections, questions, schedule and publish routes.</summary>
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

        exams.MapGet("/", ListExams)
            .RequireAuthorization(ExamAuthoringPermissions.Read)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListExams")
            .WithDescription("List the newest exams");

        exams.MapGet("/{examId:guid}", GetExam)
            .RequireAuthorization(ExamAuthoringPermissions.Read)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetExam")
            .WithDescription("Get an exam with its sections and questions");

        exams.MapPut("/{examId:guid}/schedule", ScheduleExam)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ScheduleExam")
            .WithDescription("Set the exam's window, duration and late-entry cutoff");

        exams.MapPut("/{examId:guid}/scope", SetScope)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("SetExamScope")
            .WithDescription("Limit the exam's questions to a book or chosen chapters, or lift the limit");

        exams.MapPut("/{examId:guid}/result-release", SetResultRelease)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("SetExamResultRelease")
            .WithDescription("Choose when candidates may see which of their answers were right: right after submitting, at a set time, or when released");

        exams.MapPost("/{examId:guid}/results/release", ReleaseResults)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ReleaseExamResults")
            .WithDescription("Show candidates which of their answers were right, for an exam set to manual release");

        exams.MapPost("/{examId:guid}/sections", AddSection)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("AddExamSection")
            .WithDescription("Add a section to a draft exam");

        exams.MapDelete("/{examId:guid}/sections/{sectionId:guid}", RemoveSection)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("RemoveExamSection")
            .WithDescription("Remove a section and the places its questions held from a draft exam; the questions stay in the bank");

        exams.MapPost("/{examId:guid}/sections/{sectionId:guid}/questions", AddQuestion)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("AddExamQuestion")
            .WithDescription("Add a question from the bank to a section of a draft exam");

        exams.MapDelete("/{examId:guid}/sections/{sectionId:guid}/questions/{questionId:guid}", RemoveQuestion)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("RemoveExamQuestion")
            .WithDescription("Take a question out of a section of a draft exam; the question stays in the bank");

        exams.MapPost("/{examId:guid}/publish", PublishExam)
            .RequireAuthorization(ExamAuthoringPermissions.Publish)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("PublishExam")
            .WithDescription("Publish a scheduled draft exam so invited candidates can take it");
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
            user.GetUserId(),
            request.Scope?.ToInput());

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/exams/{result.Id}", result);
    }

    private static async Task<IResult> ListExams(ListExamsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    private static async Task<IResult> GetExam(Guid examId, GetExamHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, ct));

    private static async Task<IResult> ScheduleExam(
        Guid examId, ScheduleExamRequest request, ScheduleExamHandler handler, CancellationToken ct)
    {
        var durationSeconds = request.DurationMinutes is { } minutes ? checked(minutes * 60) : (int?)null;
        var command = new ScheduleExamCommand(
            examId,
            request.ScheduledStartTime,
            request.ScheduledEndTime,
            request.TimeZone,
            request.LateEntryDeadline,
            durationSeconds);

        return Results.Ok(await handler.HandleAsync(command, ct));
    }

    private static async Task<IResult> SetScope(
        Guid examId, ExamScopeRequest request, SetExamScopeHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new SetExamScopeCommand(examId, request.ToInput()), ct));

    private static async Task<IResult> SetResultRelease(
        Guid examId, ResultReleaseRequest request, SetResultReleaseHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new SetResultReleaseCommand(examId, request.Mode, request.ReleaseTime), ct));

    private static async Task<IResult> ReleaseResults(Guid examId, ReleaseResultsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, ct));

    private static async Task<IResult> AddSection(
        Guid examId, AddSectionRequest request, AddSectionHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new AddSectionCommand(examId, request.Name, request.TimeSeconds), ct);
        return Results.Created($"/v1/exams/{examId}/sections/{result.Id}", result);
    }

    private static async Task<IResult> RemoveSection(
        Guid examId, Guid sectionId, RemoveSectionHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(new RemoveSectionCommand(examId, sectionId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddQuestion(
        Guid examId, Guid sectionId, AddQuestionRequest request, AddExamQuestionHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new AddExamQuestionCommand(examId, sectionId, request.QuestionId), ct);
        return Results.Created($"/v1/exams/{examId}/sections/{sectionId}/questions/{result.Id}", result);
    }

    private static async Task<IResult> RemoveQuestion(
        Guid examId, Guid sectionId, Guid questionId, RemoveExamQuestionHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(new RemoveExamQuestionCommand(examId, sectionId, questionId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> PublishExam(Guid examId, PublishExamHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, ct));
}

/// <summary>Request DTO for creating an exam. The creator is the caller, so it is not part of the body.</summary>
/// <param name="SeriesId">The exam series the exam belongs to; omit or send null for a standalone exam.</param>
/// <param name="Name">Display name of the exam.</param>
/// <param name="Description">Optional longer description shown to candidates.</param>
/// <param name="Scope">What the exam's questions may be drawn from; omit for anywhere in the question bank.</param>
public record CreateExamRequest(
    Guid? SeriesId,
    string Name,
    string? Description,
    ExamScopeRequest? Scope = null);

/// <summary>Request body for limiting an exam to a book or chosen chapters.</summary>
/// <param name="Type">Independent (anywhere), Book (any chapter of one book) or Chapters (chosen chapters of one book).</param>
/// <param name="BookId">The book, for Book and Chapters.</param>
/// <param name="ChapterIds">The chosen chapters, for Chapters.</param>
public record ExamScopeRequest(ExamScopeType Type, Guid? BookId = null, IReadOnlyList<Guid>? ChapterIds = null)
{
    /// <summary>The application-layer form of the request.</summary>
    public ExamScopeInput ToInput() => new(Type, BookId, ChapterIds);
}

/// <summary>Request body for scheduling an exam; every instant is UTC.</summary>
/// <param name="ScheduledStartTime">When the window opens.</param>
/// <param name="ScheduledEndTime">When the window closes.</param>
/// <param name="TimeZone">The IANA time zone the exam is described in; optional.</param>
/// <param name="LateEntryDeadline">The last moment a candidate may still start; optional.</param>
/// <param name="DurationMinutes">How long one attempt lasts, in minutes; optional (the whole window when omitted).</param>
public record ScheduleExamRequest(
    DateTime? ScheduledStartTime,
    DateTime? ScheduledEndTime,
    string? TimeZone,
    DateTime? LateEntryDeadline,
    int? DurationMinutes);

/// <summary>Request body for choosing when candidates may see which of their answers were right; the time is UTC.</summary>
/// <param name="Mode">Instant (right after submitting), Scheduled (from <paramref name="ReleaseTime"/>) or Manual (when released by an administrator).</param>
/// <param name="ReleaseTime">From when the answers are visible; required for Scheduled, ignored otherwise.</param>
public record ResultReleaseRequest(ResultReleaseMode? Mode, DateTime? ReleaseTime);

/// <summary>Request body for adding a section.</summary>
/// <param name="Name">The section's name.</param>
/// <param name="TimeSeconds">An optional time limit for the section.</param>
public record AddSectionRequest(string? Name, int? TimeSeconds);

/// <summary>Request body for adding a bank question to a section.</summary>
/// <param name="QuestionId">The question's id in the question bank.</param>
public record AddQuestionRequest(Guid QuestionId);
