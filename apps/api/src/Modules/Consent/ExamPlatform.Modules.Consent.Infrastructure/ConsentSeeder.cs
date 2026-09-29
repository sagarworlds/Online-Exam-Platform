using ExamPlatform.Modules.Consent.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Consent.Infrastructure;

/// <summary>
/// Idempotently seeds an initial notice version per purpose, so a consent
/// cannot be recorded against a purpose with no notice text to point at.
/// Runs at Host startup; safe on every startup since it no-ops once seeded.
/// </summary>
public static class ConsentSeeder
{
    /// <summary>Seeds one initial notice version per <see cref="ConsentPurpose"/> if none exist yet.</summary>
    /// <param name="context">The Consent module's database context.</param>
    /// <param name="nowUtc">The current instant, used as each seeded notice's effective-from date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SeedAsync(ConsentDbContext context, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (await context.NoticeVersions.AnyAsync(cancellationToken))
        {
            return;
        }

        context.NoticeVersions.AddRange(
            NoticeVersion.Create(ConsentPurpose.TermsOfService, "v1", nowUtc, "https://example.invalid/legal/terms-v1"),
            NoticeVersion.Create(ConsentPurpose.PrivacyNotice, "v1", nowUtc, "https://example.invalid/legal/privacy-v1"),
            NoticeVersion.Create(ConsentPurpose.ProctoringDataProcessing, "v1", nowUtc, "https://example.invalid/legal/proctoring-v1"));

        await context.SaveChangesAsync(cancellationToken);
    }
}
