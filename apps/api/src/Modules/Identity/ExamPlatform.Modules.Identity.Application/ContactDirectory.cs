using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>The <see cref="IContactDirectory"/> other modules ask how to reach an account's holder.</summary>
public sealed class ContactDirectory(IUserRepository users) : IContactDirectory
{
    /// <inheritdoc />
    public async Task<string?> FindPhoneNumberByEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var user = await users.GetByEmailAsync(email.Trim(), cancellationToken);
        return user is { Status: UserStatus.Active } && !string.IsNullOrWhiteSpace(user.PhoneNumber) ? user.PhoneNumber : null;
    }

    /// <inheritdoc />
    public async Task<Guid?> FindActiveAccountIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var user = await users.GetByEmailAsync(email.Trim(), cancellationToken);
        return user is { Status: UserStatus.Active } ? user.Id : null;
    }
}
