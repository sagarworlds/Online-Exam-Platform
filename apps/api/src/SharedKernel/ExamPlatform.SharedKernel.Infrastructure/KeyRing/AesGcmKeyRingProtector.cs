using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>
/// Protects each key ring entry with AES-256-GCM under a key-encryption key that is held outside the database (NFR-5, #86). The whole entry
/// is encrypted, so nothing about the ring (not the key material, and not its dates or algorithms) is readable from a copy of the database.
/// </summary>
/// <remarks>
/// <para>
/// Why the whole entry and not only the secret inside it, which is what Data Protection's own <c>ProtectKeysWith*</c> options do: those
/// leave the keys that were stored in the clear before the change exactly as they are, and Data Protection has no supported way to rewrite
/// them. Wrapping the stored entry keeps every key unchanged, so everything already encrypted stays readable, and an entry in the clear is
/// recognised by its element name and rewritten in place (<see cref="Reprotect"/>).
/// </para>
/// <para>
/// A fresh random 96-bit nonce is used for each entry, which is safe for the few entries a ring ever holds. The envelope's format version is
/// bound into the associated data, so an entry cannot be passed off as another format. A retired key opens an entry but never protects one.
/// An entry that cannot be opened raises <see cref="KeyRingProtectionException"/>; it is never skipped.
/// </para>
/// </remarks>
public sealed class AesGcmKeyRingProtector : IKeyRingProtector
{
    // AES-GCM is specified for a 96-bit nonce and a 128-bit tag; these are the sizes .NET's implementation is used with.
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Bound into every entry. Changing the format means changing this and the envelope version together.
    private static readonly byte[] AssociatedData = "ExamPlatform.KeyRing.v1"u8.ToArray();

    private readonly byte[] _currentKey;
    private readonly string _currentKeyId;
    private readonly Dictionary<string, byte[]> _keysById = new(StringComparer.Ordinal);

    /// <summary>Creates a protector from the current key and any retired ones.</summary>
    /// <param name="currentKey">The key that protects new and rewritten entries: 32 bytes.</param>
    /// <param name="previousKeys">Retired keys, 32 bytes each. They open entries still protected with them and never protect one.</param>
    /// <exception cref="ArgumentException">A key is not 32 bytes.</exception>
    /// <exception cref="PlatformNotSupportedException">AES-GCM is not available on this machine (it needs the platform's crypto library).</exception>
    public AesGcmKeyRingProtector(byte[] currentKey, IEnumerable<byte[]> previousKeys)
    {
        ArgumentNullException.ThrowIfNull(currentKey);
        ArgumentNullException.ThrowIfNull(previousKeys);

        // Checked here so an unsupported platform fails at startup with its own message, and not on the first entry with a generic one.
        if (!AesGcm.IsSupported)
            throw new PlatformNotSupportedException("AES-GCM is not supported on this platform, so the key ring cannot be protected.");

        _currentKey = Copy(currentKey);
        _currentKeyId = KeyRingKeyParser.IdOf(_currentKey);
        _keysById[_currentKeyId] = _currentKey;
        foreach (var previous in previousKeys)
            _keysById.TryAdd(KeyRingKeyParser.IdOf(previous), Copy(previous));
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public XElement Protect(XElement entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var plaintext = Encoding.UTF8.GetBytes(entry.ToString(SaveOptions.DisableFormatting));
        try
        {
            // nonce | tag | ciphertext, in one buffer.
            var payload = new byte[NonceSize + TagSize + plaintext.Length];
            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var ciphertext = payload.AsSpan(NonceSize + TagSize);

            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(_currentKey, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);

            return new XElement(
                KeyRingEnvelope.ElementName,
                new XAttribute(KeyRingEnvelope.VersionAttribute, KeyRingEnvelope.CurrentVersion),
                new XAttribute(KeyRingEnvelope.KeyIdAttribute, _currentKeyId),
                Convert.ToBase64String(payload));
        }
        finally
        {
            // The plaintext holds the key material; do not leave this copy of it lying in memory.
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <inheritdoc />
    public XElement Unprotect(XElement stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        // Stored in the clear before protection was switched on. It is read as it is, and rewritten by Reprotect.
        if (!KeyRingEnvelope.IsEnvelope(stored))
            return stored;

        var version = (string?)stored.Attribute(KeyRingEnvelope.VersionAttribute);
        if (version != KeyRingEnvelope.CurrentVersion)
        {
            throw new KeyRingProtectionException(
                $"A key ring entry has envelope version '{version}', which this build does not know. Run a build that is at least as new as the one that wrote it.");
        }

        var keyId = (string?)stored.Attribute(KeyRingEnvelope.KeyIdAttribute)
            ?? throw new KeyRingProtectionException("A key ring entry is damaged: it does not say which key protects it.");
        if (!_keysById.TryGetValue(keyId, out var key))
        {
            // Said at length because it is the mistake that matters: the wrong or lost key. Data Protection is stopped from carrying on, since
            // an empty ring would make it create a new key, and everything protected under the keys it could not read would be lost.
            throw new KeyRingProtectionException(
                $"A key ring entry is protected with key {keyId}, which this deployment does not have. Set {KeyRingProtectionOptions.KeySettingName} to that key, "
                + $"or list it under {KeyRingProtectionOptions.PreviousKeysSettingName}. The host stops here instead of making a new key, "
                + "because that would leave everything protected so far unreadable.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(stored.Value);
        }
        catch (FormatException ex)
        {
            throw new KeyRingProtectionException("A key ring entry is damaged: its payload is not valid base64.", ex);
        }

        if (payload.Length < NonceSize + TagSize)
            throw new KeyRingProtectionException("A key ring entry is damaged: its payload is too short to hold a nonce and a tag.");

        var plaintext = new byte[payload.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize),
                plaintext,
                AssociatedData);
            return XElement.Parse(Encoding.UTF8.GetString(plaintext));
        }
        catch (CryptographicException ex)
        {
            throw new KeyRingProtectionException(
                $"A key ring entry protected with key {keyId} could not be opened: it was altered, or it was written under a different key that has the same id.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <inheritdoc />
    public string? Reprotect(string storedXml)
    {
        ArgumentNullException.ThrowIfNull(storedXml);

        XElement stored;
        try
        {
            stored = XElement.Parse(storedXml);
        }
        catch (XmlException ex)
        {
            throw new KeyRingProtectionException("A stored key ring entry is not valid XML.", ex);
        }

        // Already under the current key: leave it, so a run on a protected ring changes nothing.
        if (KeyRingEnvelope.IsEnvelope(stored) && (string?)stored.Attribute(KeyRingEnvelope.KeyIdAttribute) == _currentKeyId)
            return null;

        return Protect(Unprotect(stored)).ToString(SaveOptions.DisableFormatting);
    }

    private static byte[] Copy(byte[] key)
    {
        if (key.Length != KeyRingProtectionOptions.KeySizeBytes)
            throw new ArgumentException($"A key-encryption key must be {KeyRingProtectionOptions.KeySizeBytes} bytes.", nameof(key));

        return (byte[])key.Clone();
    }
}
