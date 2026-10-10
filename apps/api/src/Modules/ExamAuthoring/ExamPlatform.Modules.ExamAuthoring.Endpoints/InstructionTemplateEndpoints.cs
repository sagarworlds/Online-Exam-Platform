using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.ExamAuthoring.Endpoints;

/// <summary>
/// Maps the instruction template routes and the routes that set an exam's instructions (FR-41). Reading templates needs <c>exam.read</c>;
/// every change needs <c>exam.manage</c>, the permission the exam editor uses.
/// </summary>
public static class InstructionTemplateEndpoints
{
    /// <summary>Maps <c>/v1/instruction-templates</c> and the exam routes that set or copy an exam's instructions.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapInstructionTemplateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var templates = endpoints.MapGroup("/v1/instruction-templates")
            .WithTags("ExamAuthoring")
            .RequireAuthorization();

        templates.MapGet("/", ListTemplates)
            .RequireAuthorization(ExamAuthoringPermissions.Read)
            .WithName("ListInstructionTemplates")
            .WithDescription("List the instruction templates staff can start an exam's instructions from");

        templates.MapPost("/", CreateTemplate)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .WithName("CreateInstructionTemplate")
            .WithDescription("Create an instruction template");

        templates.MapPut("/{templateId:guid}", UpdateTemplate)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .WithName("UpdateInstructionTemplate")
            .WithDescription("Change an instruction template; exams that already copied it keep their text");

        templates.MapDelete("/{templateId:guid}", DeleteTemplate)
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .WithName("DeleteInstructionTemplate")
            .WithDescription("Delete an instruction template; exams that already copied it keep their text");

        endpoints.MapPut("/v1/exams/{examId:guid}/instructions", SetExamInstructions)
            .WithTags("ExamAuthoring")
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .WithName("SetExamInstructions")
            .WithDescription("Set the instructions a candidate reads before starting a draft exam");

        endpoints.MapPost("/v1/exams/{examId:guid}/instructions/from-template/{templateId:guid}", UseTemplateForExam)
            .WithTags("ExamAuthoring")
            .RequireAuthorization(ExamAuthoringPermissions.Manage)
            .WithName("UseInstructionTemplateForExam")
            .WithDescription("Copy an instruction template's text into a draft exam's instructions");
    }

    private static async Task<IResult> ListTemplates(ListInstructionTemplatesHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct));

    private static async Task<IResult> CreateTemplate(
        InstructionTemplateRequest request, CreateInstructionTemplateHandler handler, CancellationToken ct)
    {
        var template = await handler.HandleAsync(new CreateInstructionTemplateCommand(request.Title, request.Body), ct);
        return Results.Created($"/v1/instruction-templates/{template.Id}", template);
    }

    private static async Task<IResult> UpdateTemplate(
        Guid templateId, InstructionTemplateRequest request, UpdateInstructionTemplateHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new UpdateInstructionTemplateCommand(templateId, request.Title, request.Body), ct));

    private static async Task<IResult> DeleteTemplate(
        Guid templateId, DeleteInstructionTemplateHandler handler, CancellationToken ct)
    {
        await handler.HandleAsync(new DeleteInstructionTemplateCommand(templateId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetExamInstructions(
        Guid examId, ExamInstructionsRequest request, SetExamInstructionsHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new SetExamInstructionsCommand(examId, request.Instructions), ct));

    private static async Task<IResult> UseTemplateForExam(
        Guid examId, Guid templateId, UseInstructionTemplateHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(new UseInstructionTemplateCommand(examId, templateId), ct));
}

/// <summary>Request body for creating or changing an instruction template.</summary>
/// <param name="Title">The template's name; required.</param>
/// <param name="Body">The instructions text; required.</param>
public record InstructionTemplateRequest(string? Title, string? Body);

/// <summary>Request body for setting an exam's instructions by hand.</summary>
/// <param name="Instructions">The instructions text; blank or omitted clears it.</param>
public record ExamInstructionsRequest(string? Instructions);
