namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>Rules about <see cref="OtpPurpose"/> values.</summary>
public static class OtpPurposeExtensions
{
    /// <summary>
    /// Whether staff may read the code of a challenge with this purpose. Limited to the candidate
    /// sign-in and registration codes, so support can help someone whose email or SMS never
    /// arrived. The staff second factor is excluded on purpose: an administrator able to read it
    /// would defeat the point of having one. Password reset is excluded because it is not a code
    /// at all but a link tied to the account owner.
    /// </summary>
    /// <param name="purpose">The purpose to check.</param>
    public static bool IsRevealableToStaff(this OtpPurpose purpose) =>
        purpose is OtpPurpose.Login or OtpPurpose.Registration;
}
