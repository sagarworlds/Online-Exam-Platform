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
}
