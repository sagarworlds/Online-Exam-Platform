using System.Security.Cryptography;
using System.Text;

namespace ExamPlatform.Modules.Guardian.Domain;

/// <summary>
/// The one-time code a guardian receives to confirm a candidate link (FR-39, NFR-5). Issue and check codes only through this
/// type, so the stored form is always the same hash.
/// </summary>
public static class GuardianLinkToken
{
    /// <summary>How long a guardian has to confirm a link. Long enough for a parent to act on an e-mail; short enough that an old one stops working.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>Issues a new code, which the e-mail carries, and its hash, which is all the platform keeps.</summary>
    /// <param name="nowUtc">When the code is issued; its lifetime starts here.</param>
    /// <returns>The raw code, its hash, and when it stops working.</returns>
    public static IssuedLinkToken Issue(DateTime nowUtc)
    {
        // 256 random bits are far beyond guessing, so a plain hash is enough: no salt, and no lockout is needed to stop guessing.
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return new IssuedLinkToken(raw, Hash(raw), nowUtc + Lifetime);
    }

    /// <summary>The stored form of a code: its SHA-256 as 64 lowercase hex characters.</summary>
    /// <param name="rawToken">The code as the guardian presents it.</param>
    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
