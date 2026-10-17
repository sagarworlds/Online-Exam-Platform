namespace ExamPlatform.Modules.Invite.Application.Dtos;

/// <summary>What a change of address did.</summary>
/// <param name="Changed">How many invitations now carry the new address.</param>
public sealed record ChangeInviteEmailResultDto(int Changed);
