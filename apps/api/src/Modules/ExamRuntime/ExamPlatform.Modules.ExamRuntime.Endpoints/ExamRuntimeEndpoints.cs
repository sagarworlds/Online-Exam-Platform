using System.Security.Claims;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints;

/// <summary>Body of <c>POST /v1/me/exams/{examId}/attempts</c>; the body may be left out when resuming.</summary>
/// <param name="InstructionsAcknowledged">Whether the candidate confirmed they read the instructions. A new attempt is refused without it.</param>
public sealed record StartAttemptRequest(bool InstructionsAcknowledged = false);

/// <summary>Body of <c>PUT /v1/me/attempts/{attemptId}/answers/{questionId}</c>.</summary>
/// <param name="OptionId">The option the candidate chose, for a question that takes one answer.</param>
/// <param name="OptionIds">The options the candidate chose, for a multiple-answer question; when given, it is used instead of <paramref name="OptionId"/>.</param>
public sealed record SaveAnswerRequest(Guid? OptionId = null, IReadOnlyList<Guid>? OptionIds = null);

/// <summary>Body of <c>POST /v1/exams/{examId}/candidates/{candidateId}/extra-attempts</c>.</summary>
/// <param name="Reason">Why the candidate is being given another attempt; optional, at most 500 characters.</param>
public sealed record GrantExtraAttemptRequest(string? Reason);

/// <summary>Body of <c>PUT /v1/exams/{examId}/candidates/{candidateId}/accommodation</c> (FR-49): the whole accommodation, not a patch.</summary>
/// <param name="ExtraTimeMinutes">Minutes added to the candidate's deadline, 0 to 720; omitted means none.</param>
/// <param name="ReaderScribe">Whether the candidate may use a reader or scribe; omitted means no.</param>
/// <param name="AlternateFormats">Formats of the exam page they get: <c>large_text</c>, <c>high_contrast</c>, <c>screen_reader</c>; omitted means none.</param>
/// <param name="Notes">A note for staff, at most 500 characters. Shown to staff only.</param>
public sealed record SetAccommodationRequest(
    int ExtraTimeMinutes = 0, bool ReaderScribe = false, IReadOnlyList<string?>? AlternateFormats = null, string? Notes = null);

/// <summary>Body of <c>POST /v1/me/attempts/{attemptId}/focus-violations</c>.</summary>
/// <param name="Kind">How the candidate left the page: <c>TabHidden</c>, <c>WindowBlurred</c> or <c>FullscreenExited</c>.</param>
public sealed record FocusViolationRequest(string? Kind);

/// <summary>Body of <c>POST /v1/exams/{examId}/attempts/{attemptId}/warn</c>.</summary>
/// <param name="Message">What to tell the candidate, 1 to 500 characters.</param>
public sealed record WarnAttemptRequest(string? Message);

/// <summary>Request body for rescoring a submitted attempt.</summary>
/// <param name="Reason">Why; the candidate is shown it with the change.</param>
public sealed record RescoreAttemptRequest(string? Reason);

/// <summary>Body of <c>POST /v1/exams/{examId}/attempts/{attemptId}/terminate</c> and <c>/invalidate</c>.</summary>
/// <param name="Reason">Why, 1 to 500 characters; the candidate is shown it.</param>
public sealed record AttemptActionReasonRequest(string? Reason);

/// <summary>Body of <c>POST /v1/me/exams/{examId}/attempt-requests</c>.</summary>
/// <param name="Message">Why the candidate wants another attempt; optional, at most 500 characters.</param>
public sealed record RequestAttemptRequest(string? Message);

/// <summary>Body of <c>POST /v1/attempt-requests/{requestId}/decline</c>.</summary>
/// <param name="Note">A reason the candidate will see; optional, at most 500 characters.</param>
public sealed record DeclineAttemptRequestRequest(string? Note);

/// <summary>Body of <c>POST /v1/me/attempts/{attemptId}/disputes</c>.</summary>
/// <param name="QuestionId">The question whose answer key the candidate disputes.</param>
/// <param name="Reason">Why they think the key is wrong; required, at most 1000 characters.</param>
public sealed record RaiseDisputeRequest(Guid QuestionId, string? Reason);

/// <summary>Body of <c>POST /v1/disputes/{disputeId}/reject</c>.</summary>
/// <param name="Note">Why the answer key stands; required, at most 500 characters. The candidate sees it.</param>
public sealed record RejectDisputeRequest(string? Note);

/// <summary>Body of <c>POST /v1/me/attempts/{attemptId}/issues</c>.</summary>
/// <param name="Category">What kind of problem it is: <c>Question</c>, <c>Technical</c> or <c>Other</c>, in any case.</param>
/// <param name="Message">What is wrong; required, at most 1000 characters.</param>
/// <param name="QuestionId">The question on screen, when the report is about one; optional.</param>
public sealed record ReportIssueRequest(string? Category, string? Message, Guid? QuestionId);

/// <summary>Body of <c>POST /v1/issue-reports/{issueReportId}/resolve</c>.</summary>
/// <param name="Note">What was done; optional, at most 500 characters.</param>
public sealed record ResolveIssueReportRequest(string? Note);

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

        me.MapPost("/exams/{examId:guid}/attempt-requests", RequestAttempt)
            .Produces<MyAttemptRequestDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("RequestAttempt")
            .WithDescription("Ask an administrator for one more attempt at an exam, once every attempt held has been used");

        me.MapGet("/attempts/{attemptId:guid}", GetAttempt)
            .Produces<AttemptDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetAttempt")
            .WithDescription("Read an attempt: its questions while open, its score once submitted. With lite=true the pictures in the questions are left out, each as a marker to fetch with GetQuestionPicture (low-bandwidth mode)");

        me.MapGet("/attempts/{attemptId:guid}/questions/{questionId:guid}/pictures/{key}", GetQuestionPicture)
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("GetQuestionPicture")
            .WithDescription("One picture of a question in an open attempt, named by the key its marker carries (low-bandwidth mode)");

        me.MapGet("/attempts/{attemptId:guid}/status", GetAttemptStatus)
            .Produces<AttemptStatusDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetAttemptStatus")
            .WithDescription("The exam page's heartbeat: whether the attempt is open or paused, its deadline, and any warnings, without the questions");

        me.MapGet("/attempts/{attemptId:guid}/review", GetAttemptReview)
            .Produces<AttemptReviewDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("GetAttemptReview")
            .WithDescription("Read a submitted attempt with which answers were right, once the exam's author has released them");

        me.MapPost("/attempts/{attemptId:guid}/disputes", RaiseDispute)
            .Produces<MyDisputeDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("RaiseDispute")
            .WithDescription("Dispute the answer key of one question in a released result, within the dispute window; once per question per attempt");

        me.MapPost("/attempts/{attemptId:guid}/issues", ReportIssue)
            .Produces<MyIssueReportDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithName("ReportIssue")
            .WithDescription("Report a problem from inside the exam being sat (FR-42): a question, something technical, or anything else; the attempt carries on");

        me.MapPut("/attempts/{attemptId:guid}/answers/{questionId:guid}", SaveAnswer)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("SaveAnswer")
            .WithDescription("Save the option chosen for one question of an open attempt");

        me.MapDelete("/attempts/{attemptId:guid}/answers/{questionId:guid}", ClearAnswer)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("ClearAnswer")
            .WithDescription("Take back the option chosen for one question of an open attempt, so it counts as unanswered");

        me.MapPut("/attempts/{attemptId:guid}/marks/{questionId:guid}", MarkQuestion)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("MarkQuestion")
            .WithDescription("Mark one question of an open attempt for review; marking a marked question changes nothing");

        me.MapDelete("/attempts/{attemptId:guid}/marks/{questionId:guid}", UnmarkQuestion)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("UnmarkQuestion")
            .WithDescription("Take the review mark off one question of an open attempt; unmarking an unmarked question changes nothing");

        me.MapPut("/attempts/{attemptId:guid}/section/{sectionId:guid}", MoveToSection)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("MoveToSection")
            .WithDescription("Move to a later section of an open attempt; when the exam locks sections the earlier one cannot be returned to");

        me.MapPost("/attempts/{attemptId:guid}/focus-violations", RecordFocusViolation)
            .Produces<FocusViolationResultDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("RecordFocusViolation")
            .WithDescription("Report that the candidate left the exam page; the server counts it and ends the attempt when the exam's limit is reached");

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

        // What an administrator can do to an attempt in progress or just finished (FR-29). Each route names the exam as well as the
        // attempt, so an attempt of another exam is a 404, and each is audited by the events the attempt raises.
        exams.MapPost("/{examId:guid}/attempts/{attemptId:guid}/rescore", RescoreAttempt)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptSummaryDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("RescoreAttempt")
            .WithDescription("Score a submitted attempt again from the answers stored; a change in the score is kept as a revision with the reason");

        exams.MapPost("/{examId:guid}/attempts/{attemptId:guid}/warn", WarnAttempt)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptSummaryDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("WarnAttempt")
            .WithDescription("Send the candidate a warning while they sit the exam; their page shows it within seconds");

        exams.MapPost("/{examId:guid}/attempts/{attemptId:guid}/pause", PauseAttempt)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptSummaryDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("PauseAttempt")
            .WithDescription("Pause an attempt in progress: the candidate cannot answer and the clock stops");

        exams.MapPost("/{examId:guid}/attempts/{attemptId:guid}/resume", ResumeAttempt)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptSummaryDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("ResumeAttempt")
            .WithDescription("Resume a paused attempt; its deadline moves later by the time it was paused");

        exams.MapPost("/{examId:guid}/attempts/{attemptId:guid}/terminate", TerminateAttempt)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptSummaryDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("TerminateAttempt")
            .WithDescription("End an attempt early, scored with the answers saved so far; the candidate is shown the reason");

        exams.MapPost("/{examId:guid}/attempts/{attemptId:guid}/invalidate", InvalidateAttempt)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptSummaryDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("InvalidateAttempt")
            .WithDescription("Invalidate a finished attempt's result so it no longer counts; the candidate is shown the reason instead of a score");

        // Staff see the exam as a candidate would (FR-15). Read-only: nothing is stored, so it needs no attempt and uses nobody's allowance.
        exams.MapGet("/{examId:guid}/preview", PreviewExam)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("PreviewExam")
            .WithDescription("Show the exam as a candidate would see it, in any state; nothing is saved");

        // Where an attempt was sat from (FR-26): the starting address and device, and each change. Staff only.
        exams.MapGet("/{examId:guid}/attempts/{attemptId:guid}/clients", ListAttemptClients)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<IReadOnlyList<AttemptClientDto>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ListAttemptClients")
            .WithDescription("Show where one attempt was sat from: its starting IP address and device signature, and each change of either");

        exams.MapGet("/{examId:guid}/attempts/{attemptId:guid}/paper", GetAttemptPaper)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptPaperDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetAttemptPaper")
            .WithDescription("Show the questions one attempt consisted of, marking those drawn for the candidate");

        exams.MapPost("/{examId:guid}/candidates/{candidateId:guid}/extra-attempts", GrantExtraAttempt)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<ExamCandidateDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("GrantExtraAttempt")
            .WithDescription("Give one enrolled candidate one more attempt at an exam, once they have used the ones they hold");

        // What a candidate is allowed because of a disability or another need (FR-49): staff only, since it describes a person's need.
        exams.MapPut("/{examId:guid}/candidates/{candidateId:guid}/accommodation", SetAccommodation)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<ExamCandidateDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("SetAccommodation")
            .WithDescription("Give an enrolled candidate extra time, a reader or scribe and alternate formats at an exam, or change what they have; extra time reaches an attempt already in progress");

        exams.MapDelete("/{examId:guid}/candidates/{candidateId:guid}/accommodation", RemoveAccommodation)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<ExamCandidateDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("RemoveAccommodation")
            .WithDescription("Take a candidate's accommodation away for the attempts that start afterwards; one in progress keeps what it was given");

        var disputes = endpoints.MapGroup("/v1/disputes").WithTags("ExamRuntime").RequireAuthorization();

        disputes.MapGet("/", ListDisputes)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<IReadOnlyList<DisputeDto>>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListDisputes")
            .WithDescription("List candidates' answer-key disputes, open ones unless a status is given, oldest first");

        disputes.MapPost("/{disputeId:guid}/reject", RejectDispute)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<DisputeDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("RejectDispute")
            .WithDescription("Leave the answer key as it is and tell the candidate why; correcting the key settles its disputes the other way");

        var issueReports = endpoints.MapGroup("/v1/issue-reports").WithTags("ExamRuntime").RequireAuthorization();

        issueReports.MapGet("/", ListIssueReports)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<IReadOnlyList<IssueReportDto>>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListIssueReports")
            .WithDescription("List the problems candidates reported from inside an exam, open ones unless a status is given, oldest first");

        issueReports.MapPost("/{issueReportId:guid}/resolve", ResolveIssueReport)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<IssueReportDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("ResolveIssueReport")
            .WithDescription("Mark a reported problem as dealt with, optionally saying what was done");

        var requests = endpoints.MapGroup("/v1/attempt-requests").WithTags("ExamRuntime").RequireAuthorization();

        requests.MapGet("/", ListAttemptRequests)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<IReadOnlyList<AttemptRequestDto>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ListAttemptRequests")
            .WithDescription("List candidates' requests for another attempt, waiting ones unless a status is given, oldest first");

        requests.MapPost("/{requestId:guid}/approve", ApproveAttemptRequest)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptRequestDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("ApproveAttemptRequest")
            .WithDescription("Give the candidate the attempt they asked for");

        requests.MapPost("/{requestId:guid}/decline", DeclineAttemptRequest)
            .RequireAuthorization(ExamRuntimePermissions.ManageAttempts)
            .Produces<AttemptRequestDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithName("DeclineAttemptRequest")
            .WithDescription("Turn a request down, optionally saying why");
    }

    private static async Task<IResult> RequestAttempt(
        Guid examId, RequestAttemptRequest? request, ClaimsPrincipal user, RequestAttemptHandler handler, CancellationToken ct)
    {
        var created = await handler.HandleAsync(examId, user.GetUserId(), request?.Message, ct);
        return Results.Created($"/v1/me/exams", created);
    }

    private static async Task<IResult> RaiseDispute(
        Guid attemptId, RaiseDisputeRequest request, ClaimsPrincipal user, RaiseDisputeHandler handler, CancellationToken ct)
    {
        var raised = await handler.HandleAsync(attemptId, user.GetUserId(), request.QuestionId, request.Reason, ct);
        return Results.Created($"/v1/me/attempts/{attemptId}/review", raised);
    }

    private static async Task<IResult> ListDisputes(string? status, ListDisputesHandler handler, CancellationToken ct)
    {
        // Parsed here, ignoring case, because the framework's own enum binding is case-sensitive; a status that is not one of ours
        // is a mistake to report, not a reason to quietly show the open ones.
        DisputeStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DisputeStatus>(status.Trim(), ignoreCase: true, out var value) || !Enum.IsDefined(value))
                throw new InvalidAttemptError("The status must be open, accepted or rejected.");
            parsed = value;
        }

        return Results.Ok(await handler.HandleAsync(parsed, ct));
    }

    private static async Task<IResult> RejectDispute(
        Guid disputeId, RejectDisputeRequest? request, ClaimsPrincipal user, RejectDisputeHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(disputeId, user.GetUserId(), request?.Note, ct));

    private static async Task<IResult> ReportIssue(
        Guid attemptId, ReportIssueRequest request, ClaimsPrincipal user, ReportIssueHandler handler, CancellationToken ct)
    {
        // Parsed here, ignoring case, because the framework's own enum binding is case-sensitive; a category that is not one of ours is
        // a mistake to report, not a reason to file it under something else.
        if (!Enum.TryParse<IssueCategory>(request.Category?.Trim(), ignoreCase: true, out var category) || !Enum.IsDefined(category))
            throw new InvalidAttemptError("Choose what kind of problem it is: Question, Technical or Other.");

        var reported = await handler.HandleAsync(attemptId, user.GetUserId(), category, request.QuestionId, request.Message, ct);
        return Results.Created($"/v1/me/attempts/{attemptId}", reported);
    }

    private static async Task<IResult> ListIssueReports(string? status, ListIssueReportsHandler handler, CancellationToken ct)
    {
        IssueReportStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<IssueReportStatus>(status.Trim(), ignoreCase: true, out var value) || !Enum.IsDefined(value))
                throw new InvalidAttemptError("The status must be open or resolved.");
            parsed = value;
        }

        return Results.Ok(await handler.HandleAsync(parsed, ct));
    }

    private static async Task<IResult> ResolveIssueReport(
        Guid issueReportId, ResolveIssueReportRequest? request, ClaimsPrincipal user, ResolveIssueReportHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(issueReportId, user.GetUserId(), request?.Note, ct));

    private static async Task<IResult> ListAttemptRequests(string? status, ListAttemptRequestsHandler handler, CancellationToken ct)
    {
        // Parsed here, ignoring case, because the framework's own enum binding is case-sensitive; a status that is not one of ours
        // is a mistake to report, not a reason to quietly show the waiting ones.
        AttemptRequestStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AttemptRequestStatus>(status.Trim(), ignoreCase: true, out var value) || !Enum.IsDefined(value))
                throw new InvalidAttemptError("The status must be pending, approved or declined.");
            parsed = value;
        }

        return Results.Ok(await handler.HandleAsync(parsed, ct));
    }

    private static async Task<IResult> ApproveAttemptRequest(Guid requestId, ClaimsPrincipal user, ApproveAttemptRequestHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(requestId, user.GetUserId(), ct));

    private static async Task<IResult> DeclineAttemptRequest(
        Guid requestId, DeclineAttemptRequestRequest? request, ClaimsPrincipal user, DeclineAttemptRequestHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(requestId, user.GetUserId(), request?.Note, ct));

    private static async Task<IResult> RescoreAttempt(
        Guid examId, Guid attemptId, RescoreAttemptRequest? request, RescoreAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, attemptId, request?.Reason, ct));

    private static async Task<IResult> WarnAttempt(
        Guid examId, Guid attemptId, WarnAttemptRequest? request, ClaimsPrincipal user, WarnAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, attemptId, user.GetUserId(), request?.Message, ct));

    private static async Task<IResult> PauseAttempt(Guid examId, Guid attemptId, PauseAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, attemptId, ct));

    private static async Task<IResult> ResumeAttempt(Guid examId, Guid attemptId, ResumeAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, attemptId, ct));

    private static async Task<IResult> TerminateAttempt(
        Guid examId, Guid attemptId, AttemptActionReasonRequest? request, ClaimsPrincipal user, TerminateAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, attemptId, user.GetUserId(), request?.Reason, ct));

    private static async Task<IResult> InvalidateAttempt(
        Guid examId, Guid attemptId, AttemptActionReasonRequest? request, ClaimsPrincipal user, InvalidateAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, attemptId, user.GetUserId(), request?.Reason, ct));

    private static async Task<IResult> GetAttemptStatus(Guid attemptId, ClaimsPrincipal user, GetAttemptStatusHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(attemptId, user.GetUserId(), ct));

    private static async Task<IResult> PreviewExam(Guid examId, ClaimsPrincipal user, PreviewExamHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, user.GetUserId(), ct));

    private static async Task<IResult> ListAttemptClients(Guid examId, Guid attemptId, ListAttemptClientsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, attemptId, ct));

    private static async Task<IResult> GetAttemptPaper(Guid examId, Guid attemptId, GetAttemptPaperHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, attemptId, ct));

    private static async Task<IResult> ListExamAttempts(Guid examId, ListExamAttemptsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, ct));

    private static async Task<IResult> GrantExtraAttempt(
        Guid examId, Guid candidateId, GrantExtraAttemptRequest? request, ClaimsPrincipal user, GrantExtraAttemptHandler handler, CancellationToken ct)
    {
        var row = await handler.HandleAsync(examId, candidateId, user.GetUserId(), request?.Reason, ct);
        return Results.Created($"/v1/exams/{examId}/attempts", row);
    }

    private static async Task<IResult> SetAccommodation(
        Guid examId, Guid candidateId, SetAccommodationRequest request, ClaimsPrincipal user, SetAccommodationHandler handler, CancellationToken ct) =>
        // Minutes in the API, seconds in the model; clamped so an absurd number reaches the domain's range check instead of overflowing.
        Results.Ok(await handler.HandleAsync(
            new SetAccommodationCommand(
                examId, candidateId, user.GetUserId(), (int)Math.Clamp((long)request.ExtraTimeMinutes * 60, int.MinValue, int.MaxValue),
                request.ReaderScribe, request.AlternateFormats, request.Notes),
            ct));

    private static async Task<IResult> RemoveAccommodation(Guid examId, Guid candidateId, RemoveAccommodationHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(examId, candidateId, ct));

    private static async Task<IResult> ListMyExams(ClaimsPrincipal user, MyExamsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(user.GetUserId(), ct));

    private static async Task<IResult> StartAttempt(
        Guid examId, StartAttemptRequest? request, ClaimsPrincipal user, StartAttemptHandler handler, CancellationToken ct) =>
        // No body means "not acknowledged": resuming needs none, but a new attempt is then refused.
        Results.Ok(await handler.HandleAsync(examId, user.GetUserId(), request?.InstructionsAcknowledged ?? false, ct));

    private static async Task<IResult> GetAttempt(Guid attemptId, bool? lite, ClaimsPrincipal user, GetAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(attemptId, user.GetUserId(), ct, lite == true));

    private static async Task<IResult> GetQuestionPicture(
        Guid attemptId, Guid questionId, string key, ClaimsPrincipal user, GetQuestionPictureHandler handler, HttpResponse response, CancellationToken ct)
    {
        var picture = await handler.HandleAsync(attemptId, user.GetUserId(), questionId, key, ct);

        // A question's picture does not change within an attempt (its version is pinned), and only its candidate may read it, so the browser may keep it
        // for the sitting: a picture fetched once is never fetched again over a slow connection.
        response.Headers.CacheControl = "private, max-age=86400";
        return Results.File(picture.Bytes, picture.ContentType);
    }

    private static async Task<IResult> GetAttemptReview(Guid attemptId, ClaimsPrincipal user, GetAttemptReviewHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(attemptId, user.GetUserId(), ct));

    private static async Task<IResult> SaveAnswer(
        Guid attemptId, Guid questionId, SaveAnswerRequest request, ClaimsPrincipal user, SaveAnswerHandler handler, CancellationToken ct)
    {
        // Both shapes are accepted: clients written before multiple-answer questions send one optionId. Neither is an invalid answer.
        IReadOnlyCollection<Guid> chosen = request.OptionIds is { Count: > 0 } ids ? ids
            : request.OptionId is { } id ? new[] { id }
            : Array.Empty<Guid>();
        await handler.HandleAsync(attemptId, user.GetUserId(), questionId, chosen, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ClearAnswer(
        Guid attemptId, Guid questionId, ClaimsPrincipal user, ClearAnswerHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(attemptId, user.GetUserId(), questionId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkQuestion(
        Guid attemptId, Guid questionId, ClaimsPrincipal user, MarkQuestionHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(attemptId, user.GetUserId(), questionId, marked: true, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnmarkQuestion(
        Guid attemptId, Guid questionId, ClaimsPrincipal user, MarkQuestionHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(attemptId, user.GetUserId(), questionId, marked: false, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MoveToSection(
        Guid attemptId, Guid sectionId, ClaimsPrincipal user, MoveToSectionHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(attemptId, user.GetUserId(), sectionId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RecordFocusViolation(
        Guid attemptId, FocusViolationRequest request, ClaimsPrincipal user, RecordFocusViolationHandler handler, CancellationToken ct)
    {
        // Numbers parse as enum values too, and an undefined one would be stored as a kind nobody can read, so only the names count.
        if (!Enum.TryParse<FocusViolationKind>(request.Kind, ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind)
            || int.TryParse(request.Kind, out _))
            throw new InvalidAttemptError("Say how the page was left: TabHidden, WindowBlurred or FullscreenExited.");

        return Results.Ok(await handler.HandleAsync(attemptId, user.GetUserId(), kind, ct));
    }

    private static async Task<IResult> SubmitAttempt(Guid attemptId, ClaimsPrincipal user, SubmitAttemptHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(attemptId, user.GetUserId(), ct));
}
