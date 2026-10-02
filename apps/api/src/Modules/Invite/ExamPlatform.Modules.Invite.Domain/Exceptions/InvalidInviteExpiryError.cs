using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>A code lifetime outside the range the platform allows.</summary>
/// <param name="minHours">The shortest allowed lifetime.</param>
/// <param name="maxHours">The longest allowed lifetime.</param>
public sealed class InvalidInviteExpiryError(int minHours, int maxHours)
    : DomainException($"An invite code must live between {minHours} and {maxHours} hours.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_invite_expiry";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
