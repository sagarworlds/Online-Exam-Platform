using ExamPlatform.Modules.Batch.Application.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Batch.Endpoints;

/// <summary>Maps the Batch module's HTTP endpoints (FR-17, FR-18, FR-19).</summary>
public static class BatchEndpoints
{
    /// <summary>Maps <c>POST /v1/batches</c> and other batch management endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapBatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var batches = endpoints.MapGroup("/v1/batches").WithTags("Batch").RequireAuthorization();

        batches.MapPost("/", CreateBatch)
            .WithName("CreateBatch")
            .WithDescription("Create a new batch");

        batches.MapPost("/{batchId}/members", AddBatchMember)
            .WithName("AddBatchMember")
            .WithDescription("Add a member to a batch");

        batches.MapPost("/{batchId}/activate", ActivateBatch)
            .WithName("ActivateBatch")
            .WithDescription("Activate a batch");

        batches.MapPost("/{batchId}/close", CloseBatch)
            .WithName("CloseBatch")
            .WithDescription("Close a batch");
    }

    private static async Task<IResult> CreateBatch(
        CreateBatchRequest request,
        CreateBatchHandler handler,
        CancellationToken ct)
    {
        var command = new CreateBatchCommand(
            request.ExamId,
            request.Name,
            request.Description,
            request.MaxMembers,
            request.CreatedBy);

        var result = await handler.HandleAsync(command, ct);
        return Results.Created($"/v1/batches/{result.Id}", result);
    }

    private static async Task<IResult> AddBatchMember(
        Guid batchId,
        AddBatchMemberRequest request,
        AddBatchMemberHandler handler,
        CancellationToken ct)
    {
        var command = new AddBatchMemberCommand(
            batchId,
            request.Email,
            request.Phone);

        await handler.HandleAsync(command, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ActivateBatch(
        Guid batchId,
        ActivateBatchHandler handler,
        CancellationToken ct)
    {
        var command = new ActivateBatchCommand(batchId);
        await handler.HandleAsync(command, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CloseBatch(
        Guid batchId,
        CloseBatchHandler handler,
        CancellationToken ct)
    {
        var command = new CloseBatchCommand(batchId);
        await handler.HandleAsync(command, ct);
        return Results.NoContent();
    }
}

/// <summary>Request DTO for creating a batch.</summary>
public record CreateBatchRequest(
    Guid ExamId,
    string Name,
    string? Description,
    int MaxMembers,
    Guid CreatedBy);

/// <summary>Request DTO for adding a batch member.</summary>
public record AddBatchMemberRequest(
    string Email,
    string? Phone = null);
