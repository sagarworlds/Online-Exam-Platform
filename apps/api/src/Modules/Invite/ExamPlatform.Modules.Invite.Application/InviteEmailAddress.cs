using System.ComponentModel.DataAnnotations;
using ExamPlatform.Modules.Invite.Domain.Exceptions;

namespace ExamPlatform.Modules.Invite.Application;

/// <summary>The one rule for the address an invitation is for, so creating an invitation and changing its address agree on what is valid.</summary>
internal static class InviteEmailAddress
{
    /// <summary>Trims an address typed by staff and checks it is a valid e-mail address.</summary>
    /// <param name="raw">The address as typed; null or blank is not an address.</param>
    /// <returns>The trimmed address.</returns>
    /// <exception cref="InvalidInviteEmailError">The address is missing or not a valid e-mail address.</exception>
    public static string Parse(string? raw)
    {
        var trimmed = raw?.Trim();

        // EmailAddressAttribute treats null as valid, so a missing address is refused here before it can reach it. It also accepts spaces
        // inside the address ("a b@x.in"), which no mail server delivers to, so those are refused too.
        if (string.IsNullOrEmpty(trimmed) || trimmed.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(trimmed))
            throw new InvalidInviteEmailError();

        return trimmed;
    }

    /// <summary>Whether two addresses are the same address: invited addresses are compared without regard to case, as when accepting.</summary>
    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
