using System.Xml.Linq;

namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>
/// Stores the key ring as Data Protection wrote it, for a Development host that has no key-encryption key. The startup check allows this
/// nowhere else (<see cref="KeyRingProtectionOptionsValidator"/>), so a developer's database needs no secret and a deployment cannot end
/// up here by leaving the setting out.
/// </summary>
/// <remarks>
/// It still refuses to read a ring that is protected: a host without the key cannot open it, and passing the entries on unread would make Data
/// Protection see an empty ring and create a new key.
/// </remarks>
public sealed class UnprotectedKeyRing : IKeyRingProtector
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public XElement Protect(XElement entry) => entry;

    /// <inheritdoc />
    /// <exception cref="KeyRingProtectionException">The entry is protected, and this host has no key to open it with.</exception>
    public XElement Unprotect(XElement stored) =>
        KeyRingEnvelope.IsEnvelope(stored)
            ? throw new KeyRingProtectionException(
                $"The key ring holds protected entries, but {KeyRingProtectionOptions.KeySettingName} is not set, so this host cannot open them. "
                + "Set it to the key the ring was protected with.")
            : stored;

    /// <inheritdoc />
    public string? Reprotect(string storedXml) => null;
}
