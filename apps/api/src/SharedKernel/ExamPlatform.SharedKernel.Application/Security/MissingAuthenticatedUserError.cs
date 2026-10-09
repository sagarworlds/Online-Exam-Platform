using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.SharedKernel.Application.Security;

/// <summary>
/// The request's access token does not carry a usable identity claim (e.g. no <c>sub</c>, or one
/// that is not a GUID). Answered as a 401 so the client signs in again, instead of surfacing as the
/// <c>InvalidOperationException</c>, <c>NullReferenceException</c> or <c>FormatException</c> (a 500)
/// that reading the claim by hand would raise.
/// </summary>
public sealed class MissingAuthenticatedUserError : DomainException
{
    /// <summary>Initializes the error for a claim that is absent or malformed.</summary>
    /// <param name="claimType">The claim that was expected, e.g. <c>sub</c>. Safe to surface: it is a claim name, never a value.</param>
    public MissingAuthenticatedUserError(string claimType)
        : base($"The access token does not carry a valid '{claimType}' claim; sign in again.")
    {
        ClaimType = claimType;
    }

    /// <summary>The claim that was expected but absent or malformed.</summary>
    public string ClaimType { get; }

    /// <inheritdoc />
    public override string ErrorCode => "unauthenticated";

    /// <inheritdoc />
    public override int HttpStatusCode => 401;
}
