using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>Reads a key-encryption key from its configuration text, and names a key without revealing it.</summary>
internal static class KeyRingKeyParser
{
    /// <summary>
    /// Reads a key written in base64. Standard and URL-safe base64 are both accepted, and the padding may be missing: whichever way a host's
    /// secret generator wrote the same 32 bytes, they are the same key, so a deployment is not refused for the alphabet its generator used.
    /// </summary>
    /// <param name="text">The configuration value.</param>
    /// <param name="key">The 32 key bytes, when the text is a valid key.</param>
    /// <returns>True when the text decodes to exactly <see cref="KeyRingProtectionOptions.KeySizeBytes"/> bytes.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out byte[]? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalised = text.Trim().Replace('-', '+').Replace('_', '/');
        normalised = normalised.PadRight((normalised.Length + 3) / 4 * 4, '=');

        // One spare byte: a key that is too long then decodes to more than 32 bytes, and is refused as the wrong size. Without it the decode
        // would simply fail, which says less.
        var buffer = new byte[KeyRingProtectionOptions.KeySizeBytes + 1];
        if (!Convert.TryFromBase64String(normalised, buffer, out var written) || written != KeyRingProtectionOptions.KeySizeBytes)
        {
            CryptographicOperations.ZeroMemory(buffer);
            return false;
        }

        key = buffer[..KeyRingProtectionOptions.KeySizeBytes];
        CryptographicOperations.ZeroMemory(buffer);
        return true;
    }

    /// <summary>
    /// A short name for a key: the first eight bytes of its SHA-256 hash, in hex. It is written next to each protected entry, so the key that
    /// protected the entry can be found among the current and retired keys, and so an error can name which key is missing. Eight bytes of a
    /// hash of a random 256-bit key tell an attacker nothing useful about the key.
    /// </summary>
    /// <param name="key">The key bytes.</param>
    /// <returns>Sixteen lower-case hex characters.</returns>
    public static string IdOf(byte[] key) => Convert.ToHexStringLower(SHA256.HashData(key).AsSpan(0, 8));
}
