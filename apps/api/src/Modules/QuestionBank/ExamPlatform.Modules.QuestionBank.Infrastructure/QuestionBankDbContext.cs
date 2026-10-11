using System.Text.Json;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure;

/// <summary>
/// The QuestionBank module's persistence context, scoped to the <c>questionBank</c> Postgres schema. The content of questions (their text,
/// options, answers and version history) is stored encrypted (#57); see <see cref="ContentConverters"/>.
/// </summary>
/// <param name="options">The context options.</param>
/// <param name="cipher">The cipher that encrypts and decrypts content; its key ring lives in this database (see <see cref="QuestionContentCipher"/>).</param>
public sealed class QuestionBankDbContext(DbContextOptions<QuestionBankDbContext> options, QuestionContentCipher cipher)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>The Data Protection key ring, kept here so every replica decrypts with the same keys (#57).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>The stored questions.</summary>
    public DbSet<Question> Questions => Set<Question>();

    /// <summary>The stored books.</summary>
    public DbSet<Book> Books => Set<Book>();

    /// <summary>The stored chapters, owned by their books.</summary>
    public DbSet<Chapter> Chapters => Set<Chapter>();

    /// <summary>The stored classes, the level above books.</summary>
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();

    /// <summary>Every version any question has had (FR-7).</summary>
    public DbSet<QuestionVersion> QuestionVersions => Set<QuestionVersion>();
    public DbSet<QuestionReviewEntry> QuestionReviewEntries => Set<QuestionReviewEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("questionBank");

        modelBuilder.Entity<Question>(b =>
        {
            b.ToTable("Questions");
            b.HasKey(q => q.Id);
            // HTML that may embed images, so the unbounded text type; Question.MaxHtmlLength is the real ceiling.
            b.Property(q => q.Text).IsRequired().HasColumnType("text").HasConversion(ContentConverters.Text(cipher));
            // The plain text a search looks through. Encrypted with the stem; a search decrypts it in the application (#57).
            b.Property(q => q.SearchText).IsRequired().HasColumnType("text").HasConversion(ContentConverters.Text(cipher));
            // A hash of the stem's letters and digits, so a new question is checked against the bank by an index lookup (FR-9).
            b.Property(q => q.TextKey).IsRequired().HasMaxLength(64);
            b.HasIndex(q => q.TextKey);
            b.Property(q => q.Language).IsRequired().HasMaxLength(QuestionLanguage.MaxLength);
            // One question per language in a group, so two translators racing to add the same language cannot both succeed (FR-10).
            b.HasIndex(q => new { q.TranslationGroupId, q.Language }).IsUnique();
            // Stored as its name rather than its number, so the column reads the same in a query and survives a reordered enum.
            b.Property(q => q.Difficulty).HasConversion<string>().HasMaxLength(10);
            b.Property(q => q.Status).HasConversion<string>().HasMaxLength(10);
            // The review queue is read by status.
            b.HasIndex(q => q.Status);
            // A Postgres text[]; topics are filtered with "= ANY(...)" and listed with unnest, which a delimited string could not do.
            b.PrimitiveCollection(q => q.Topics).HasColumnType("text[]");
            // A text question: whether the candidate types the answer, and the answers a typed one may be (also a text[]).
            b.Property(q => q.IsTextAnswer).HasDefaultValue(false);
            // The answers a typed answer may be: encrypted as one value, so they are never read from a plain column (#57).
            b.Property(q => q.AcceptedAnswers)
                .HasConversion(ContentConverters.Strings(cipher), ContentConverters.StringsComparer())
                .HasColumnType("text");
            b.Ignore(q => q.DomainEvents);
            b.HasIndex(q => q.CreatedAtUtc);
            b.HasIndex(q => q.ChapterId);

            // A chapter is archived, never deleted, so nothing filed under it can be orphaned; Restrict makes the
            // database refuse a delete that would, rather than quietly unfiling the questions.
            b.HasOne<Chapter>().WithMany().HasForeignKey(q => q.ChapterId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(q => q.Options).WithOne().HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(q => q.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(q => q.Versions).WithOne().HasForeignKey(v => v.QuestionId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(q => q.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Book>(b =>
        {
            b.ToTable("Books");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Book.MaxNameLength);
            b.Property(x => x.Subject).HasMaxLength(Book.MaxSubjectLength);
            b.Property(x => x.Description).HasMaxLength(Book.MaxDescriptionLength);
            b.Ignore(x => x.DomainEvents);
            b.HasIndex(x => x.Name);

            b.HasMany(x => x.Chapters).WithOne().HasForeignKey(c => c.BookId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Chapters).HasField("_chapters").UsePropertyAccessMode(PropertyAccessMode.Field);

            // A book may sit under a class. Restrict, not cascade or clear: classes are archived, never deleted, so a class that
            // still has books must never disappear from under them.
            b.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchoolClass>(b =>
        {
            b.ToTable("Classes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(SchoolClass.MaxNameLength);
            b.Ignore(x => x.DomainEvents);
            // Names are checked ignoring case by the handler; this index turns two requests racing with the very same name into a conflict.
            b.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Chapter>(b =>
        {
            b.ToTable("Chapters");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).IsRequired().HasMaxLength(Book.MaxChapterTitleLength);
            // Two requests adding a chapter at the same moment would otherwise both take the next position (or the
            // same title); the unique indexes turn that race into a conflict the caller can retry.
            b.HasIndex(x => new { x.BookId, x.Order }).IsUnique();
            b.HasIndex(x => new { x.BookId, x.Title }).IsUnique();
        });

        modelBuilder.Entity<QuestionOption>(b =>
        {
            b.ToTable("QuestionOptions");
            b.HasKey(o => o.Id);
            // Encrypted, so the stored value is longer than the text: the length limit is enforced by the domain, not the column.
            b.Property(o => o.Text).IsRequired().HasColumnType("text").HasConversion(ContentConverters.Text(cipher));
        });

        // A QuestionVersion is append-only (never updated after insert), so its comparers only need to satisfy EF's change-tracking
        // snapshot requirement, not support mutation.
        modelBuilder.Entity<QuestionVersion>(b =>
        {
            b.ToTable("QuestionVersions");
            b.HasKey(v => v.Id);
            b.Property(v => v.Text).IsRequired().HasColumnType("text").HasConversion(ContentConverters.Text(cipher));
            // The options a version held are encrypted JSON now, so the column is text, not jsonb (#57).
            b.Property(v => v.Options)
                .HasConversion(ContentConverters.VersionOptions(cipher), ContentConverters.VersionOptionsComparer())
                .HasColumnType("text");
            b.Property(v => v.IsTextAnswer).HasDefaultValue(false);
            b.Property(v => v.AcceptedAnswers)
                .HasConversion(ContentConverters.Strings(cipher), ContentConverters.StringsComparer())
                .HasColumnType("text");
            // A question's versions are always listed in order, and never looked up any other way.
            b.HasIndex(v => new { v.QuestionId, v.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<QuestionReviewEntry>(b =>
        {
            b.ToTable("QuestionReviewEntries");
            b.HasKey(e => e.Id);
            b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.StatusAfter).HasConversion<string>().HasMaxLength(10);
            b.Property(e => e.ByLabel).IsRequired().HasMaxLength(QuestionReviewEntry.MaxByLabelLength);
            b.Property(e => e.Comment).IsRequired().HasMaxLength(QuestionReviewEntry.MaxCommentLength);
            // A thread is read in order for one question, and goes with the question if it is deleted.
            b.HasIndex(e => new { e.QuestionId, e.CreatedAtUtc });
            b.HasOne<Question>().WithMany().HasForeignKey(e => e.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
