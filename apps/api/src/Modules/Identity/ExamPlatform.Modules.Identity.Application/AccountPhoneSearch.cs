using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Contracts;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>The <see cref="IAccountPhoneSearch"/> other modules ask which accounts hold a phone number that matches a search.</summary>
public sealed class AccountPhoneSearch(IUserRepository users) : IAccountPhoneSearch
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> FindAccountsByPhoneAsync(IReadOnlyCollection<Guid> accountIds, string phone, CancellationToken cancellationToken)
    {
        var forms = PhoneSearch.Forms(phone);
        if (forms.Count == 0 || accountIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        // Nothing here is logged: the search and the numbers are personal data, and only the ids of the matches leave this method.
        var numbers = await users.ListPhoneNumbersAsync(accountIds, cancellationToken);
        return numbers.Where(entry => PhoneSearch.Matches(entry.Value, forms)).Select(entry => entry.Key).ToHashSet();
    }
}
