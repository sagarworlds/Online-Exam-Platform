using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Invite.Application;

/// <summary>
/// Moves every invitation sent to one address to another, in one save. It is the one place that does it: the staff action on the
/// Invitations page reaches it through <see cref="Commands.ChangeInviteEmailHandler"/>, and Identity reaches it through
/// <see cref="IInviteAddressChanger"/> when staff change the address on a student's account.
/// </summary>
public sealed class InviteAddressChanger(
    IInviteRepository repository, IInviteUnitOfWork unitOfWork, Clock clock, ILogger<InviteAddressChanger> logger)
    : IInviteAddressChanger
{
    /// <inheritdoc />
    /// <exception cref="InvalidInviteEmailError">The new address is missing or not a valid e-mail address.</exception>
    public async Task<int> ChangeAddressAsync(string currentEmail, string newEmail, CancellationToken cancellationToken)
    {
        // Only the new address has to be valid. The current one is just what to look for: an invitation can only have been sent to a valid
        // address, so one that is blank or unreadable matches nothing, which is the same answer as any address nobody was invited at.
        // (An account registered through the API is not checked for the shape of its address, and editing it must not fail on that.)
        var replacement = InviteEmailAddress.Parse(newEmail);
        var current = currentEmail?.Trim();
        if (string.IsNullOrEmpty(current))
            return 0;

        // The same address, however it is written, is not a move.
        if (InviteEmailAddress.Same(current, replacement))
            return 0;

        var invites = await repository.ListByEmailAsync(current, cancellationToken);
        if (invites.Count == 0)
            return 0;

        var nowUtc = clock.UtcNow;
        foreach (var invite in invites)
            invite.ChangeEmail(replacement, nowUtc);

        // One save for all of them, so a student's invitations never end up split between two addresses by a failure part way through.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The count only: an address in a log is personal data (NFR-6).
        logger.LogInformation("The address on {Count} invitations was changed.", invites.Count);
        return invites.Count;
    }
}
