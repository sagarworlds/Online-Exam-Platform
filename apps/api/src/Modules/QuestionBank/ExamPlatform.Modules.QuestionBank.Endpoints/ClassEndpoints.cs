using System.Security.Claims;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.QuestionBank.Endpoints;

/// <summary>Maps the classes endpoints: the level above books (a class such as the 4th holds its books).</summary>
public static class ClassEndpoints
{
    /// <summary>Maps <c>/v1/classes</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapClassEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The same permissions as books: whoever writes questions organises them, and reading needs only the read permission,
        // since a reviewer's question list names the class a question is filed under.
        var classes = endpoints.MapGroup("/v1/classes")
            .WithTags("QuestionBank")
            .RequireAuthorization(QuestionBankPermissions.Manage);

        classes.MapPost("/", CreateClass).WithName("CreateClass").WithDescription("Create a class, the level above books");
        classes.MapPut("/{classId:guid}", RenameClass).WithName("RenameClass").WithDescription("Rename a class");
        classes.MapPost("/{classId:guid}/archive", ArchiveClass).WithName("ArchiveClass").WithDescription("Archive a class: kept, but closed to new books");
        classes.MapPost("/{classId:guid}/restore", RestoreClass).WithName("RestoreClass").WithDescription("Restore an archived class");

        var readers = endpoints.MapGroup("/v1/classes")
            .WithTags("QuestionBank")
            .RequireAuthorization(QuestionBankPermissions.Read);

        readers.MapGet("/", ListClasses).WithName("ListClasses").WithDescription("List classes with their number of books; archived ones only when asked for");
    }

    private static async Task<IResult> CreateClass(ClassRequest request, ClaimsPrincipal user, CreateClassHandler handler, CancellationToken ct)
    {
        var created = await handler.HandleAsync(new CreateClassCommand(request.Name, user.GetUserId()), ct);
        return Results.Created($"/v1/classes/{created.Id}", created);
    }

    private static async Task<IResult> ListClasses(ListClassesHandler handler, bool? includeArchived, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(includeArchived ?? false, ct));

    private static async Task<IResult> RenameClass(Guid classId, ClassRequest request, ChangeClassHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.RenameAsync(new RenameClassCommand(classId, request.Name), ct));

    private static async Task<IResult> ArchiveClass(Guid classId, ChangeClassHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.ArchiveAsync(classId, ct));

    private static async Task<IResult> RestoreClass(Guid classId, ChangeClassHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.RestoreAsync(classId, ct));
}

/// <summary>Request body for creating or renaming a class. The author is the caller, so it is not part of the body.</summary>
/// <param name="Name">The class's name, for example "4th".</param>
public sealed record ClassRequest(string? Name);
