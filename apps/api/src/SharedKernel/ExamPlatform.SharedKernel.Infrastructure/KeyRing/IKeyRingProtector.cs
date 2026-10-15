using System.Xml.Linq;

namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>
/// Protects the entries of the Data Protection key ring before they are stored, and opens them when they are read (NFR-5, #86). There is one
/// implementation that encrypts (<see cref="AesGcmKeyRingProtector"/>) and one that does not (<see cref="UnprotectedKeyRing"/>), which only a
/// Development host may end up with.
/// </summary>
public interface IKeyRingProtector
{
    /// <summary>Whether entries are encrypted. False only in a Development host that has no key configured.</summary>
    bool IsEnabled { get; }

    /// <summary>Protects one key ring entry for storage.</summary>
    /// <param name="entry">The entry as Data Protection wrote it: a <c>key</c> or a <c>revocation</c> element.</param>
    /// <returns>The element to store. It holds no part of the entry in the clear.</returns>
    XElement Protect(XElement entry);

    /// <summary>
    /// Opens one stored entry. An entry that was stored in the clear, before protection was switched on, is returned as it is, so a ring
    /// written before this change still works until it has been rewritten (<see cref="Reprotect"/>).
    /// </summary>
    /// <param name="stored">The element as it was stored.</param>
    /// <returns>The entry as Data Protection wrote it.</returns>
    /// <exception cref="KeyRingProtectionException">
    /// The entry is protected with a key this deployment does not have, was altered, or is damaged. The ring is never read as empty instead,
    /// because Data Protection would then create a new key and everything protected under the old keys would become unreadable.
    /// </exception>
    XElement Unprotect(XElement stored);

    /// <summary>
    /// Works out the stored text an entry should have under the current key: the entry in the clear is protected, and an entry protected with
    /// a retired key is rewritten under the current one.
    /// </summary>
    /// <param name="storedXml">The stored text of one entry.</param>
    /// <returns>The text to store instead, or null when the entry is already protected with the current key and needs no change.</returns>
    /// <exception cref="KeyRingProtectionException">The entry cannot be opened (see <see cref="Unprotect"/>) or is not valid XML.</exception>
    string? Reprotect(string storedXml);
}
