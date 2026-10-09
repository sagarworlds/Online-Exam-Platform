using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Why a sign-in attempt produced no code or no session, for a developer to read.</summary>
public enum SignInHint
{
    /// <summary>No account has the address: a typo, or it never registered.</summary>
    NoAccountForAddress,

    /// <summary>The account is suspended or deactivated.</summary>
    AccountLocked,

    /// <summary>A staff account asked for a code on its own; staff sign in with their password and then a code.</summary>
    StaffMustUsePasswordAndCode,

    /// <summary>The account has no password, which is every candidate's case: candidates sign in with a one-time code.</summary>
    CandidateHasNoPassword,

    /// <summary>The password does not match.</summary>
    WrongPassword,
}

/// <summary>
/// Tells a developer why a sign-in produced nothing. A real code is only ever sent to an address that may have one, and every
/// other attempt is answered exactly the same way, so a caller (or anyone reading production logs) cannot tell which addresses
/// have accounts (FR-1, NFR-5). That leaves a developer at a terminal with a verify screen and no code to type. The adapter used
/// with the development log says why; every other adapter must say nothing.
/// </summary>
public interface ISignInDiagnostics
{
    /// <summary>Reports why an attempt got no code or no session.</summary>
    /// <param name="hint">The reason.</param>
    /// <param name="channel">How the address would have been reached.</param>
    /// <param name="destination">The email address or phone number the caller gave.</param>
    void Explain(SignInHint hint, OtpChannel channel, string destination);
}
