using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.SharedKernel.Infrastructure.KeyRing;

/// <summary>
/// Wraps the repository that stores the Data Protection key ring, so every entry is protected before it is stored and opened after it is
/// read (NFR-5, #86). Data Protection itself neither knows nor cares: it sees the same <c>key</c> and <c>revocation</c> elements as before.
/// </summary>
/// <remarks>
/// <para>
/// A read that cannot open an entry throws and is never answered with fewer entries. Data Protection treats a ring with no usable key as a
/// new installation and creates one, and everything protected under the keys that were hidden would then fail to open.
/// </para>
/// <para>
/// The protector is looked up when the ring is first read or written, not when this repository is built. Building Data Protection's services
/// reads no keys, so a job that never touches the ring (the migrate-and-seed job on an empty database, which is meant to need only a
/// connection string) does not need the key either.
/// </para>
/// </remarks>
/// <param name="inner">The repository that stores the entries, such as the database one.</param>
/// <param name="protectorFactory">Supplies the protector on first use. It may throw, for example when the key is missing outside Development.</param>
/// <param name="logger">Where the one-time notice about unprotected entries is written.</param>
public sealed class ProtectedXmlRepository(IXmlRepository inner, Func<IKeyRingProtector> protectorFactory, ILogger<ProtectedXmlRepository> logger)
    : IXmlRepository
{
    private int _warnedAboutUnprotectedEntries;

    /// <inheritdoc />
    /// <exception cref="KeyRingProtectionException">An entry cannot be opened with the keys this host has.</exception>
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var protector = protectorFactory();
        var stored = inner.GetAllElements();

        var opened = new List<XElement>(stored.Count);
        var inTheClear = 0;
        foreach (var entry in stored)
        {
            if (!KeyRingEnvelope.IsEnvelope(entry))
                inTheClear++;

            opened.Add(protector.Unprotect(entry));
        }

        // Said once per process, not on every refresh of the ring. The migrate-and-seed step rewrites these entries.
        if (inTheClear > 0 && protector.IsEnabled && Interlocked.Exchange(ref _warnedAboutUnprotectedEntries, 1) == 0)
        {
            logger.LogWarning(
                "{Count} key ring entries are stored unprotected. They are read as they are, and the migrate-and-seed step protects them with {Setting}.",
                inTheClear,
                KeyRingProtectionOptions.KeySettingName);
        }

        return opened;
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName) =>
        inner.StoreElement(protectorFactory().Protect(element), friendlyName);
}
