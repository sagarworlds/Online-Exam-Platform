using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>
/// The account holds a role with mandatory 2FA (FR-3), so a one-time code on its own
/// cannot sign it in: it must use its password followed by the second-factor code.
/// </summary>
public sealed class TwoFactorLoginRequiredError()
    : DomainException("This account must sign in with its password followed by a one-time code.")
{
    /// <inheritdoc />
    public override string ErrorCode => "two_factor_login_required";

    /// <inheritdoc />
    public override int HttpStatusCode => 403;
}
