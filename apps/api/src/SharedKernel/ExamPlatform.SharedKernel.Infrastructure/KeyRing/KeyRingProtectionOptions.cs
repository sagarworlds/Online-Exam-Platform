namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>
/// The key that wraps every entry of the Data Protection key ring, and the keys it replaced (NFR-5, #86). It is read from configuration,
/// which on Render is an environment variable, so the key is never stored in the database.
/// </summary>
/// <remarks>
/// Why it exists: question content is encrypted with Data Protection keys, and those keys are kept in the same Postgres database as the
/// ciphertext. A copy of the database alone would hold both, so it would open the content. With this key the ring entries are ciphertext
/// too, and a database copy opens nothing without the key that sits outside it.
/// <para>
/// The key is required outside Development (see <see cref="KeyRingProtectionOptionsValidator"/>). Losing it loses the ring, and with the ring
/// the question content, so a copy has to be kept somewhere safe and apart from the database.
/// </para>
/// </remarks>
public sealed class KeyRingProtectionOptions
{
    /// <summary>The configuration section, <c>DataProtection</c>. On Render the variables are <c>DataProtection__KeyEncryptionKey</c> and so on.</summary>
    public const string SectionName = "DataProtection";

    /// <summary>The setting that holds the current key, as it is written in configuration. Named in messages so an operator knows what to set.</summary>
    public const string KeySettingName = SectionName + ":KeyEncryptionKey";

    /// <summary>The setting that holds the retired keys, as it is written in configuration.</summary>
    public const string PreviousKeysSettingName = SectionName + ":PreviousKeyEncryptionKeys";

    /// <summary>The size of a key, in bytes: AES-256.</summary>
    public const int KeySizeBytes = 32;

    /// <summary>
    /// The current key-encryption key: 32 random bytes written in base64 (<c>openssl rand -base64 32</c> makes one). New and rewritten key ring
    /// entries are protected with it. Blank only in a Development host, where the ring is then stored unprotected.
    /// </summary>
    public string? KeyEncryptionKey { get; set; }

    /// <summary>
    /// Keys that were current before, in the same form. They open entries that are still protected with them, and never protect a new one.
    /// To rotate: put the new key in <see cref="KeyEncryptionKey"/> and the old one here, and start the host; the entries are rewritten under the
    /// new key, after which the old key can be removed from here.
    /// </summary>
    public string[] PreviousKeyEncryptionKeys { get; set; } = [];
}
