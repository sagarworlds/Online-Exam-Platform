using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;

/// <summary>
/// Encrypts the content of questions at rest (NFR-5, #57) with ASP.NET Data Protection, and decrypts it when the bank reads it. A stored
/// value is the protected payload behind a version prefix, so a value that is still plaintext is recognisable and can be encrypted in
/// place (<see cref="QuestionContentBackfill"/>).
/// </summary>
/// <remarks>
/// The keys live in Postgres (see the QuestionBank module installer), so every replica decrypts with the same key ring. The key ring
/// sits in the same database as the ciphertext, so a copy of the database alone does not protect the content; that residual risk is stated
/// in the pull request, and the keys should be wrapped by a key outside the database before launch.
/// </remarks>
public sealed class QuestionContentCipher(IDataProtectionProvider protection)
{
    /// <summary>The purpose the protector is created for; changing it makes existing content unreadable, so it is versioned.</summary>
    public const string Purpose = "ExamPlatform.QuestionBank.Content.v1";

    /// <summary>The prefix on every encrypted value. A plaintext that happens to start with it is still encrypted by the backfill, because the backfill checks that the rest decrypts.</summary>
    public const string Prefix = "dpe1:";

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>Encrypts one value for storage.</summary>
    /// <param name="plaintext">The content, as the author or the importer wrote it.</param>
    /// <returns>The stored form, prefix included.</returns>
    public string Encrypt(string plaintext) => Prefix + _protector.Protect(plaintext);

    /// <summary>
    /// Decrypts one stored value. It refuses a value that is not encrypted, so a plaintext can never be read back as if it were content.
    /// </summary>
    /// <param name="stored">The value as it is stored.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ContentNotEncryptedException">The value has no encryption prefix: it is plaintext that the backfill has not reached yet.</exception>
    /// <exception cref="CryptographicException">The value carries the prefix but its payload was altered or was made with other keys.</exception>
    public string Decrypt(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            throw new ContentNotEncryptedException();

        return _protector.Unprotect(stored[Prefix.Length..]);
    }

    /// <summary>
    /// Whether a stored value is ciphertext this key ring can open. A value with the prefix that does not open is treated as plaintext, so
    /// a question whose text begins with the prefix is still encrypted.
    /// </summary>
    /// <param name="stored">The value as it is stored.</param>
    public bool IsCiphertext(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            _protector.Unprotect(stored[Prefix.Length..]);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

/// <summary>A stored question value is still plaintext. Raised instead of returning it, so content that was never encrypted is never served.</summary>
public sealed class ContentNotEncryptedException() : InvalidOperationException(
    "A question's content is stored as plaintext. The content backfill has to run before the API serves questions.");
