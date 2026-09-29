using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ExamPlatform.SharedKernel.Infrastructure;

/// <summary>
/// Marks every <see cref="Guid"/> primary key as client-generated (<see cref="ValueGenerated.Never"/>).
/// Every aggregate assigns its own id via <c>Guid.NewGuid()</c> in its factory method
/// (never leaving it to the database) — but EF Core's default convention for Guid
/// keys still assumes the database or a store-generated default might produce them.
/// Left unconfigured, a *new* child entity added to an already-tracked parent's
/// collection (e.g. a new <c>UserSession</c> added to a loaded <c>User</c>) gets
/// misclassified as an existing, unmodified row and EF emits an UPDATE instead of
/// an INSERT — which fails with a concurrency exception because no such row exists.
/// Every module's <c>DbContext</c> calls this from <c>OnModelCreating</c>, alongside
/// <see cref="UtcDateTimeConventions.ApplyUtcDateTimeConversion"/>.
/// </summary>
public static class ClientGeneratedKeyConventions
{
    /// <summary>Sets every Guid-typed primary key property in the model to <see cref="ValueGenerated.Never"/>.</summary>
    /// <param name="modelBuilder">The model builder being configured in a <c>DbContext.OnModelCreating</c> override.</param>
    public static void ApplyClientGeneratedGuidKeys(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey is null) continue;

            foreach (var property in primaryKey.Properties)
            {
                if (property.ClrType == typeof(Guid))
                {
                    property.ValueGenerated = ValueGenerated.Never;
                }
            }
        }
    }
}
