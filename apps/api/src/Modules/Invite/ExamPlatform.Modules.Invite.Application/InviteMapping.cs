using ExamPlatform.Modules.Invite.Application.Dtos;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Application;

/// <summary>Maps an invite to its DTO, so every handler reports one the same way.</summary>
internal static class InviteMapping
{
    /// <summary>Maps an invite; the exam name is passed in because the invite module does not own it.</summary>
    /// <param name="invite">The invite to map.</param>
    /// <param name="examName">The exam's name, when the caller has it.</param>
    public static InviteDto ToDto(this InviteAggregate invite, string? examName) =>
        new(
            invite.Id,
            invite.ExamId,
            examName,
            invite.BatchMemberId,
            invite.Email,
            invite.Status,
            invite.SentAt,
            invite.AcceptedAt,
            invite.DeclinedAt,
            invite.CreatedAt,
            invite.UpdatedAt);
}
