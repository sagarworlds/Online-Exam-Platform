using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Says nothing: for every OTP delivery that is not the development log. Explaining why a sign-in produced no code would reveal
/// which addresses have accounts, which is exactly what the identical answers to every attempt are there to hide.
/// </summary>
public sealed class NoSignInDiagnostics : ISignInDiagnostics
{
    /// <inheritdoc />
    public void Explain(SignInHint hint, OtpChannel channel, string destination)
    {
    }
}
