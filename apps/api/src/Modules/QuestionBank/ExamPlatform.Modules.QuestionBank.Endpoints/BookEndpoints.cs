using System.Security.Claims;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.QuestionBank.Endpoints;

/// <summary>Maps the books and chapters endpoints (FR-5): the structure authors file questions under.</summary>
public static class BookEndpoints
{
    /// <summary>Maps <c>/v1/books</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The same permission as the questions themselves: whoever writes questions organises them.
        var books = endpoints.MapGroup("/v1/books")
            .WithTags("QuestionBank")
            .RequireAuthorization(QuestionBankPermissions.Manage);

        books.MapPost("/", CreateBook).WithName("CreateBook").WithDescription("Create a book");
        books.MapGet("/", ListBooks).WithName("ListBooks").WithDescription("List books with their chapters; archived ones only when asked for");
        books.MapGet("/{bookId:guid}", GetBook).WithName("GetBook").WithDescription("Get one book with its chapters");
        books.MapPut("/{bookId:guid}", UpdateBook).WithName("UpdateBook").WithDescription("Change a book's name, subject and description");
        books.MapPost("/{bookId:guid}/archive", ArchiveBook).WithName("ArchiveBook").WithDescription("Archive a book: kept, but closed to new chapters");
        books.MapPost("/{bookId:guid}/restore", RestoreBook).WithName("RestoreBook").WithDescription("Restore an archived book");

        books.MapPost("/{bookId:guid}/chapters", AddChapter).WithName("AddChapter").WithDescription("Add a chapter to a book");
        books.MapPut("/{bookId:guid}/chapters/{chapterId:guid}", RenameChapter).WithName("RenameChapter").WithDescription("Rename a chapter");
        books.MapPost("/{bookId:guid}/chapters/{chapterId:guid}/archive", ArchiveChapter).WithName("ArchiveChapter").WithDescription("Archive a chapter: kept, but closed to new questions");
        books.MapPost("/{bookId:guid}/chapters/{chapterId:guid}/restore", RestoreChapter).WithName("RestoreChapter").WithDescription("Restore an archived chapter");
    }

    private static async Task<IResult> CreateBook(
        BookRequest request, ClaimsPrincipal user, CreateBookHandler handler, CancellationToken ct)
    {
        var created = await handler.HandleAsync(new CreateBookCommand(request.Name, request.Subject, request.Description, user.GetUserId()), ct);
        return Results.Created($"/v1/books/{created.Id}", created);
    }

    private static async Task<IResult> ListBooks(ListBooksHandler handler, bool? includeArchived, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(includeArchived ?? false, ct));

    private static async Task<IResult> GetBook(Guid bookId, GetBookHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(bookId, ct));

    private static async Task<IResult> UpdateBook(Guid bookId, BookRequest request, ChangeBookHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.UpdateAsync(new UpdateBookCommand(bookId, request.Name, request.Subject, request.Description), ct));

    private static async Task<IResult> ArchiveBook(Guid bookId, ChangeBookHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.ArchiveAsync(bookId, ct));

    private static async Task<IResult> RestoreBook(Guid bookId, ChangeBookHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.RestoreAsync(bookId, ct));

    private static async Task<IResult> AddChapter(Guid bookId, ChapterRequest request, ChangeBookHandler handler, CancellationToken ct) =>
        Results.Created($"/v1/books/{bookId}", await handler.AddChapterAsync(new AddChapterCommand(bookId, request.Title), ct));

    private static async Task<IResult> RenameChapter(
        Guid bookId, Guid chapterId, ChapterRequest request, ChangeBookHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.RenameChapterAsync(new RenameChapterCommand(bookId, chapterId, request.Title), ct));

    private static async Task<IResult> ArchiveChapter(Guid bookId, Guid chapterId, ChangeBookHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.ArchiveChapterAsync(bookId, chapterId, ct));

    private static async Task<IResult> RestoreChapter(Guid bookId, Guid chapterId, ChangeBookHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.RestoreChapterAsync(bookId, chapterId, ct));
}

/// <summary>Request body for creating or changing a book. The author is the caller, so it is not part of the body.</summary>
/// <param name="Name">The book's name.</param>
/// <param name="Subject">The subject it covers (for example "Maths"); optional.</param>
/// <param name="Description">A short description; optional.</param>
public sealed record BookRequest(string? Name, string? Subject, string? Description);

/// <summary>Request body for adding or renaming a chapter.</summary>
/// <param name="Title">The chapter's title.</param>
public sealed record ChapterRequest(string? Title);
