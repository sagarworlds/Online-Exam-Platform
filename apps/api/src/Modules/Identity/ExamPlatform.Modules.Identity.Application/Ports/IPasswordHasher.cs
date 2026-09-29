namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Hashes and verifies passwords. Implementations own the choice of algorithm (e.g. PBKDF2, bcrypt, Argon2).</summary>
public interface IPasswordHasher
{
    /// <summary>Produces a salted hash of a plaintext password, safe to store.</summary>
    /// <param name="password">The plaintext password.</param>
    string Hash(string password);

    /// <summary>Checks a plaintext password against a previously produced hash.</summary>
    /// <param name="password">The plaintext password supplied at login.</param>
    /// <param name="hash">The stored hash to check against.</param>
    bool Verify(string password, string hash);
}
