using System.Xml.Linq;

namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>
/// The shape of a protected key ring entry: <c>&lt;protectedKey version="1" keyId="…"&gt;base64&lt;/protectedKey&gt;</c>. The base64 holds the
/// AES-GCM nonce, tag and ciphertext of the whole entry, so its metadata (key id, dates, algorithms) is hidden as well as its key material.
/// </summary>
public static class KeyRingEnvelope
{
    /// <summary>The element name. Data Protection's own entries are <c>key</c> and <c>revocation</c>, so this never collides with one.</summary>
    public const string ElementName = "protectedKey";

    /// <summary>The attribute that names the envelope format, so a later format can be told apart.</summary>
    public const string VersionAttribute = "version";

    /// <summary>The attribute that names the key-encryption key by a short hash of it (the first eight bytes of its SHA-256, in hex), never by the key.</summary>
    public const string KeyIdAttribute = "keyId";

    /// <summary>The format this build writes and reads.</summary>
    public const string CurrentVersion = "1";

    /// <summary>Whether a stored element is a protected entry, as opposed to an entry stored in the clear.</summary>
    /// <param name="element">The element as it was stored.</param>
    public static bool IsEnvelope(XElement element) => element.Name == ElementName;
}
