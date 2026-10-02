using System.Security.Claims;
using ExamPlatform.Modules.Batch.Application.Commands;
using ExamPlatform.SharedKernel.Application.Security;
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
        // The group only demands a signed-in caller. Being signed in says nothing about being allowed to
        // run batches (a candidate is signed in too), so every route also names the permission it needs (FR-2, NFR-5).
        var batches = endpoints.MapGroup("/v1/batches")
            .WithTags("Batch")
            .RequireAuthorization();

        batches.MapPost("/", CreateBatch)
            .RequireAuthorization(BatchPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CreateBatch")
            .WithDescription("Create a new batch");

        batches.MapPost("/{batchId}/members", AddBatchMember)
            .RequireAuthorization(BatchPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("AddBatchMember")
            .WithDescription("Add a member to a batch");

        batches.MapPost("/{batchId}/activate", ActivateBatch)
            .RequireAuthorization(BatchPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("ActivateBatch")
            .WithDescription("Activate a batch");

        batches.MapPost("/{batchId}/close", CloseBatch)
            .RequireAuthorization(BatchPermissions.Manage)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("CloseBatch")
            .WithDescription("Close a batch");
    }

    private static async Task<IResult> CreateBatch(
        CreateBatchRequest request,
        ClaimsPrincipal user,
        CreateBatchHandler handler,
        CancellationToken ct)
    {
        // The creator is the authenticated caller, never a value from the body (FR-2, NFR-5).
        var command = new CreateBatchCommand(
            request.ExamId,
            request.Name,
            request.Description,
            request.MaxMembers,
            user.GetUserId());

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

/// <summary>Request DTO for creating a batch. The creator is the caller, so it is not part of the body.</summary>
/// <param name="ExamId">The exam the batch sits for.</param>
/// <param name="Name">Display name of the batch.</param>
/// <param name="Description">Optional longer description.</param>
/// <param name="MaxMembers">Capacity of the batch; must be greater than zero.</param>
public record CreateBatchRequest(
    Guid ExamId,
    string Name,
    string? Description,
    int MaxMembers);

/// <summary>Request DTO for adding a batch member.</summary>
public record AddBatchMemberRequest(
    string Email,
    string? Phone = null);
