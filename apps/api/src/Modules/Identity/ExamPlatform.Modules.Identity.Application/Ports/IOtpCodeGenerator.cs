namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Generates and hashes one-time-password codes.</summary>
public interface IOtpCodeGenerator
{
    /// <summary>Generates a new random numeric code to send to the candidate.</summary>
    string GenerateCode();

    /// <summary>
    /// Hashes a code the same way whether it was just generated (for storage) or
    /// supplied by a caller at verification time (for comparison) — the two call
    /// sites must never diverge, or every OTP would fail to match.
    /// </summary>
    /// <param name="code">The plaintext code.</param>
    string Hash(string code);
}
