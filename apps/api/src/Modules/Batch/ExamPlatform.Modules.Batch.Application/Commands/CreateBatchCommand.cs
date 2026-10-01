namespace ExamPlatform.Modules.Batch.Application.Commands;

/// <summary>Creates a new batch of candidates for an exam.</summary>
/// <param name="ExamId">The exam the batch sits for.</param>
/// <param name="Name">Display name of the batch.</param>
/// <param name="Description">Optional longer description.</param>
/// <param name="MaxMembers">Capacity of the batch; must be greater than zero.</param>
/// <param name="CreatedBy">The user that creates the batch.</param>
public sealed record CreateBatchCommand(
    Guid ExamId,
    string Name,
    string? Description,
    int MaxMembers,
    Guid CreatedBy);

/// <summary>Adds a member to a batch.</summary>
/// <param name="BatchId">The batch to add the member to.</param>
/// <param name="Email">E-mail address of the member.</param>
/// <param name="Phone">Optional phone number of the member.</param>
public sealed record AddBatchMemberCommand(
    Guid BatchId,
    string Email,
    string? Phone = null);

/// <summary>Activates a batch so that its members can be invited.</summary>
/// <param name="BatchId">The batch to activate.</param>
public sealed record ActivateBatchCommand(Guid BatchId);

/// <summary>Closes a batch so that it accepts no further members.</summary>
/// <param name="BatchId">The batch to close.</param>
public sealed record CloseBatchCommand(Guid BatchId);
