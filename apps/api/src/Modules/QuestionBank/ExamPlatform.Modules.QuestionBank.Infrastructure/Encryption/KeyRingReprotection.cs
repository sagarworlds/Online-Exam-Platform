using ExamPlatform.SharedKernel.Infrastructure.KeyRing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;

/// <summary>What a re-protection run found: how many key ring entries the database holds, and how many it rewrote.</summary>
/// <param name="Entries">The entries in the key ring table.</param>
/// <param name="Rewritten">The entries rewritten under the current key-encryption key; zero when every entry was already protected with it.</param>
public sealed record KeyRingReprotectionResult(int Entries, int Rewritten);

/// <summary>
/// Rewrites the Data Protection key ring entries that are stored in the clear, or under a retired key, so that every entry is protected with
/// the current key-encryption key (NFR-5, #86). It runs from the migrate-and-seed step, before the content backfill, so the first thing to
/// use the keys finds a protected ring.
/// </summary>
/// <remarks>
/// <para>
/// It changes only how an entry is stored, never the key inside it, so everything already encrypted with those keys stays readable. That is
/// also why it is not done by creating a new key: every encrypted question would then have to be re-encrypted.
/// </para>
/// <para>
/// It is idempotent: an entry already under the current key is left alone, so a run on a protected ring changes nothing, and two hosts
/// starting together at worst each write a valid envelope for the same entry. An empty table asks for no key at all, which keeps the
/// migrate-and-seed job on a new database needing only a connection string (ADR 0002). A database that holds entries needs the key, outside
/// Development, and the run fails with the name of the setting when it is missing.
/// </para>
/// <para>
/// A copy of the database taken before this ran still holds the keys in the clear. Protecting the ring cannot undo that; rotating the keys
/// and re-encrypting the content would, and is a separate decision (see docs/adr/0003-key-ring-protection.md).
/// </para>
/// </remarks>
/// <param name="context">The context whose database holds the key ring.</param>
/// <param name="protectorFactory">Supplies the protector, and is first called when there is an entry to examine, so an empty ring needs no key.</param>
/// <param name="logger">Where the outcome is written.</param>
public sealed class KeyRingReprotection(
    QuestionBankDbContext context,
    Func<IKeyRingProtector> protectorFactory,
    ILogger<KeyRingReprotection> logger)
{
    /// <summary>Protects every key ring entry with the current key, and reports what it did.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of entries found and the number rewritten.</returns>
    /// <exception cref="KeyRingProtectionException">An entry cannot be opened with the keys this host has, or is not valid XML. Nothing is written.</exception>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">The key-encryption key is missing or malformed outside Development.</exception>
    public async Task<KeyRingReprotectionResult> RunAsync(CancellationToken cancellationToken)
    {
        var entries = await context.DataProtectionKeys.ToListAsync(cancellationToken);
        if (entries.Count == 0)
            return new KeyRingReprotectionResult(0, 0);

        var protector = protectorFactory();
        if (!protector.IsEnabled)
            return new KeyRingReprotectionResult(entries.Count, 0);

        var rewritten = 0;
        foreach (var entry in entries)
        {
            if (entry.Xml is null)
                continue;

            string? updated;
            try
            {
                updated = protector.Reprotect(entry.Xml);
            }
            catch (KeyRingProtectionException ex)
            {
                // Which entry, so the operator can find it; the exception says what is wrong with it. Nothing has been saved yet.
                logger.LogError(ex, "Key ring entry {FriendlyName} could not be protected with the current key.", entry.FriendlyName);
                throw;
            }

            if (updated is null)
                continue;

            entry.Xml = updated;
            rewritten++;
        }

        // One save for every entry, so the table is either all rewritten or unchanged.
        if (rewritten > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Protected {Rewritten} of {Entries} Data Protection key ring entries with {Setting}.",
                rewritten,
                entries.Count,
                KeyRingProtectionOptions.KeySettingName);
        }

        return new KeyRingReprotectionResult(entries.Count, rewritten);
    }
}
