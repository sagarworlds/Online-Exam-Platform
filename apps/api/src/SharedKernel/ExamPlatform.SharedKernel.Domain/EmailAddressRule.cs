using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// The one definition of a usable e-mail address, shared by every module that takes one, so they cannot drift apart. It is deliberately
/// the framework's own check and no stricter, because tightening it would turn away addresses people were accepted with before.
/// </summary>
public static class EmailAddressRule
{
    private static readonly EmailAddressAttribute Attribute = new();

    /// <summary>
    /// Whether the text is shaped like an address, by the framework's <see cref="EmailAddressAttribute"/>: one <c>@</c>, with something
    /// before it and after it. Like the attribute it counts <see langword="null"/> as valid (no value is not a wrong value) and it allows
    /// white space inside the address, so a caller that needs an address a mail server can deliver to asks <see cref="IsDeliverable"/>.
    /// </summary>
    /// <param name="address">The text to check, as typed (not trimmed).</param>
    /// <returns>Whether the text passes the framework's check.</returns>
    public static bool IsWellFormed(string? address) => Attribute.IsValid(address);

    /// <summary>
    /// Whether the text is an address a mail server can deliver to: not empty, well formed, and without any white space. The attribute lets
    /// "a b@x.in" through, which no mail server delivers to, so a sign-in code or an invitation sent there would be lost.
    /// </summary>
    /// <param name="address">The text to check; trim it first if surrounding white space should be forgiven.</param>
    /// <returns>Whether the address is deliverable.</returns>
    public static bool IsDeliverable([NotNullWhen(true)] string? address) =>
        !string.IsNullOrEmpty(address) && !address.Any(char.IsWhiteSpace) && IsWellFormed(address);
}
