using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ExamPlatform.SharedKernel.Infrastructure;

/// <summary>
/// Applies a repo-wide convention that every <see cref="DateTime"/> column is
/// stored and read as UTC. Npgsql maps <c>timestamptz</c> columns to
/// <see cref="DateTime"/> with <see cref="DateTimeKind.Unspecified"/> on read;
/// without this, code that later compares against <c>Clock.UtcNow</c> (Kind=Utc)
/// would silently compare against the wrong instant depending on server locale.
/// Every module's <c>DbContext</c> calls this from <c>OnModelCreating</c>.
/// </summary>
public static class UtcDateTimeConventions
{
    // Expression trees (which ValueConverter's constructor requires) cannot contain
    // throw-expressions, so the validation lives in an ordinary method the expression calls.
    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : throw new InvalidOperationException(
            $"Attempted to persist a non-UTC DateTime (Kind={value.Kind}). " +
            "All timestamps must be produced via Clock.UtcNow.");

    private static DateTime? RequireUtcOrNull(DateTime? value) => value is null
        ? null
        : RequireUtc(value.Value);

    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toProvider => RequireUtc(toProvider),
        fromProvider => DateTime.SpecifyKind(fromProvider, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        toProvider => RequireUtcOrNull(toProvider),
        fromProvider => fromProvider.HasValue ? DateTime.SpecifyKind(fromProvider.Value, DateTimeKind.Utc) : null);

    /// <summary>Registers the UTC value converter for every <see cref="DateTime"/> and <see cref="DateTime"/>? property in the model.</summary>
    /// <param name="modelBuilder">The model builder being configured in a <c>DbContext.OnModelCreating</c> override.</param>
    public static void ApplyUtcDateTimeConversion(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(UtcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(NullableUtcConverter);
                }
            }
        }
    }
}
