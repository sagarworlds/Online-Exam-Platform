using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure;

/// <summary>The QuestionBank module's persistence context, scoped to the <c>questionBank</c> Postgres schema.</summary>
public sealed class QuestionBankDbContext(DbContextOptions<QuestionBankDbContext> options) : DbContext(options)
{
    /// <summary>The stored questions.</summary>
    public DbSet<Question> Questions => Set<Question>();

    /// <summary>The stored books.</summary>
    public DbSet<Book> Books => Set<Book>();

    /// <summary>The stored chapters, owned by their books.</summary>
    public DbSet<Chapter> Chapters => Set<Chapter>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("questionBank");

        modelBuilder.Entity<Question>(b =>
        {
            b.ToTable("Questions");
            b.HasKey(q => q.Id);
            // HTML that may embed images, so the unbounded text type; Question.MaxHtmlLength is the real ceiling.
            b.Property(q => q.Text).IsRequired().HasColumnType("text");
            // Stored as its name rather than its number, so the column reads the same in a query and survives a reordered enum.
            b.Property(q => q.Difficulty).HasConversion<string>().HasMaxLength(10);
            // A Postgres text[]; topics are filtered with "= ANY(...)" and listed with unnest, which a delimited string could not do.
            b.PrimitiveCollection(q => q.Topics).HasColumnType("text[]");
            b.Ignore(q => q.DomainEvents);
            b.HasIndex(q => q.CreatedAtUtc);
            b.HasIndex(q => q.ChapterId);

            // A chapter is archived, never deleted, so nothing filed under it can be orphaned; Restrict makes the
            // database refuse a delete that would, rather than quietly unfiling the questions.
            b.HasOne<Chapter>().WithMany().HasForeignKey(q => q.ChapterId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(q => q.Options).WithOne().HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(q => q.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);
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
            b.Property(o => o.Text).IsRequired().HasMaxLength(Question.MaxOptionTextLength);
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
