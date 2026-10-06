using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure;

/// <summary>The ExamRuntime module's persistence context, scoped to the <c>examRuntime</c> Postgres schema.</summary>
public sealed class ExamRuntimeDbContext(DbContextOptions<ExamRuntimeDbContext> options) : DbContext(options)
{
    /// <summary>Name of the shadow property that maps an attempt's Postgres <c>xmin</c> row version.</summary>
    internal const string RowVersionPropertyName = "RowVersion";

    /// <summary>The candidates' attempts.</summary>
    public DbSet<Attempt> Attempts => Set<Attempt>();

    /// <summary>The requests candidates made for another attempt.</summary>
    public DbSet<AttemptRequest> AttemptRequests => Set<AttemptRequest>();

    /// <summary>The extra attempts administrators have granted.</summary>
    public DbSet<ExtraAttemptGrant> ExtraAttemptGrants => Set<ExtraAttemptGrant>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("examRuntime");

        modelBuilder.Entity<Attempt>(b =>
        {
            b.ToTable("Attempts");
            b.HasKey(a => a.Id);
            b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.Score).HasPrecision(10, 3);
            b.Property(a => a.MaxScore).HasPrecision(10, 3);
            b.Ignore(a => a.DomainEvents);

            // The store, not the code, guarantees each attempt number is taken once per candidate per exam: two parallel
            // "start" calls both try for the next number and cannot both succeed, so the loser is reported as a conflict
            // instead of creating a second sitting nobody allowed. The index also serves "all of a candidate's attempts at an exam".
            b.HasIndex(a => new { a.ExamId, a.CandidateId, a.Number }).IsUnique();
            b.HasIndex(a => a.CandidateId);

            // Optimistic concurrency on xmin: two parallel submits of one attempt cannot both score it.
            b.Property<uint>(RowVersionPropertyName).IsRowVersion();

            b.HasMany(a => a.Answers).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.Answers).HasField("_answers").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(a => a.Marks).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.Marks).HasField("_marks").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(a => a.Paper).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.Paper).HasField("_paper").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(a => a.FocusViolations).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.FocusViolations).HasField("_focusViolations").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(a => a.ClientSightings).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.ClientSightings).HasField("_clientSightings").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Ignore(a => a.DeviceCount);
            b.Ignore(a => a.ClientChanges);

            b.HasMany(a => a.Warnings).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.Warnings).HasField("_warnings").UsePropertyAccessMode(PropertyAccessMode.Field);

            b.Property(a => a.TerminationReason).HasMaxLength(Attempt.MaxReasonLength);
            b.Property(a => a.AcknowledgedNotice).HasMaxLength(Attempt.MaxNoticeLength);

            // A small JSON object, question id to version number: it is read and written with the attempt and never searched.
            b.Property(a => a.QuestionVersions)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb")
                .HasConversion(
                    new ValueConverter<IReadOnlyDictionary<Guid, int>, string>(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        json => JsonSerializer.Deserialize<Dictionary<Guid, int>>(json, (JsonSerializerOptions?)null) ?? new Dictionary<Guid, int>()),
                    new ValueComparer<IReadOnlyDictionary<Guid, int>>(
                        (x, y) => x!.Count == y!.Count && !x.Except(y).Any(),
                        v => v.Aggregate(0, (hash, p) => HashCode.Combine(hash, p.Key, p.Value)),
                        v => new Dictionary<Guid, int>(v)));
            b.Property(a => a.InvalidationReason).HasMaxLength(Attempt.MaxReasonLength);
            b.Ignore(a => a.IsInvalidated);
            b.Ignore(a => a.IsTerminated);

            b.HasMany(a => a.Revisions).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(a => a.Revisions).HasField("_revisions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AttemptQuestion>(b =>
        {
            b.ToTable("AttemptQuestions");
            b.HasKey(x => x.Id);

            // A question is on a paper once. The index also serves loading an attempt's paper.
            b.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();

            // The question bank asks whether a question has been put in front of a candidate before it lets the key change.
            b.HasIndex(x => x.QuestionId);
        });

        modelBuilder.Entity<AttemptAnswer>(b =>
        {
            b.ToTable("AttemptAnswers");
            b.HasKey(x => x.Id);
            // A Postgres uuid[]: the chosen options travel with the answer, so reading an attempt is still one row per answer.
            b.PrimitiveCollection(x => x.SelectedOptionIds).HasColumnType("uuid[]");
            // The first chosen option, derived for readers that only know single answers.
            b.Ignore(x => x.SelectedOptionId);

            // At most one answer per question: saving again changes the row rather than adding another.
            b.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();

            // The question bank asks "has anyone answered this question?" before it lets the answer key change. The index
            // above starts with the attempt, so it cannot answer that; this one can.
            b.HasIndex(x => x.QuestionId);
        });

        modelBuilder.Entity<AttemptMark>(b =>
        {
            b.ToTable("AttemptMarks");
            b.HasKey(x => x.Id);

            // At most one mark per question: marking twice at once cannot leave two rows, and the loser is a harmless conflict.
            // The index also serves loading an attempt's marks.
            b.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
        });

        modelBuilder.Entity<AttemptClientSighting>(b =>
        {
            b.ToTable("AttemptClientSightings");
            b.HasKey(x => x.Id);
            b.Property(x => x.IpAddress).HasMaxLength(ExamPlatform.SharedKernel.Application.ClientInfo.MaxIpLength);
            b.Property(x => x.DeviceFingerprint).HasMaxLength(ExamPlatform.SharedKernel.Application.ClientInfo.MaxFingerprintLength);
            b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(20);

            // An attempt's sightings are read together with the attempt, oldest first.
            b.HasIndex(x => new { x.AttemptId, x.SeenAtUtc });
        });

        modelBuilder.Entity<AttemptWarning>(b =>
        {
            b.ToTable("AttemptWarnings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Message).IsRequired().HasMaxLength(AttemptWarning.MaxMessageLength);

            // An attempt's warnings are read together with the attempt, oldest first.
            b.HasIndex(x => new { x.AttemptId, x.IssuedAtUtc });
        });

        modelBuilder.Entity<AttemptFocusViolation>(b =>
        {
            b.ToTable("AttemptFocusViolations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);

            // An attempt's violations are read together with the attempt, oldest first, and counted against the exam's limit.
            b.HasIndex(x => new { x.AttemptId, x.OccurredAtUtc });
        });

        modelBuilder.Entity<AttemptResultRevision>(b =>
        {
            b.ToTable("AttemptResultRevisions");
            b.HasKey(x => x.Id);
            b.Property(x => x.PreviousScore).HasPrecision(10, 3);
            b.Property(x => x.PreviousMaxScore).HasPrecision(10, 3);
            b.Property(x => x.NewScore).HasPrecision(10, 3);
            b.Property(x => x.NewMaxScore).HasPrecision(10, 3);
            b.Property(x => x.Reason).IsRequired().HasMaxLength(500);

            // An attempt's revisions are read together with the attempt, oldest first.
            b.HasIndex(x => x.AttemptId);
        });

        modelBuilder.Entity<AttemptRequest>(b =>
        {
            b.ToTable("AttemptRequests");
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Message).HasMaxLength(AttemptRequest.MaxTextLength);
            b.Property(x => x.DecisionNote).HasMaxLength(AttemptRequest.MaxTextLength);

            // The store, not the code, keeps a candidate to one waiting request per exam: two taps of "ask" at the same moment
            // both pass the check, and only one insert survives. A decided request is history and may be repeated.
            b.HasIndex(x => new { x.ExamId, x.CandidateId }).IsUnique().HasFilter("\"Status\" = 'Pending'");
            // The staff queue lists by status, oldest first.
            b.HasIndex(x => new { x.Status, x.RequestedAtUtc });
            b.HasIndex(x => x.CandidateId);
        });

        modelBuilder.Entity<ExtraAttemptGrant>(b =>
        {
            b.ToTable("ExtraAttemptGrants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(ExtraAttemptGrant.MaxReasonLength);

            // Grants are numbered one after the last per candidate per exam, so two administrators granting at the same
            // moment both ask for the same number and only one succeeds. The index also serves counting a candidate's grants.
            b.HasIndex(x => new { x.ExamId, x.CandidateId, x.Number }).IsUnique();
        });

        modelBuilder.ApplyUtcDateTimeConversion();
        modelBuilder.ApplyClientGeneratedGuidKeys();
    }
}
