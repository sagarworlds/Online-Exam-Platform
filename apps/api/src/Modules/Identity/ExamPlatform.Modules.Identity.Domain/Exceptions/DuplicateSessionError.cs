using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>
/// Raised when a login is attempted while another session is already active and
/// the caller asked not to silently supersede it (<c>allowSupersede: false</c> on
/// <see cref="User.StartNewSession"/>). The default candidate login flow allows
/// supersession (FR-4's "kill the old session" behaviour); this is reserved for
/// a future, stricter flow that instead wants to reject the new login outright.
/// </summary>
public sealed class DuplicateSessionError() : DomainException("An active session already exists for this user.")
{
    /// <inheritdoc />
    public override string ErrorCode => "duplicate_session";
}
