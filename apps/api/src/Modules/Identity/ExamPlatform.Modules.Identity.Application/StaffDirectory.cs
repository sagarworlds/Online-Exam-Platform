using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Contracts;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>The <see cref="IStaffDirectory"/> other modules ask about the people who hold a permission.</summary>
public sealed class StaffDirectory(IUserRepository users) : IStaffDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetEmailsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken)
    {
        // A blank code would match nobody, but asking for it is a bug in the caller, so it is reported instead of answered with "no one".
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        return await users.ListActiveEmailsWithPermissionAsync(permissionCode.Trim(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StaffRecipient>> GetActiveRecipientsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken)
    {
        // Same rule as the e-mail lookup above: a blank code is a caller's bug, reported rather than answered with "no one".
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        var rows = await users.ListActiveWithPermissionAsync(permissionCode.Trim(), cancellationToken);
        return rows.Select(r => new StaffRecipient(r.UserId, r.Email ?? string.Empty)).ToList();
    }
}
