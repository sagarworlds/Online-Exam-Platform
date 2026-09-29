namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// Base type for domain entities: objects distinguished by identity rather than
/// by the values of their properties.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    /// <summary>The entity's unique identifier.</summary>
    public Guid Id { get; protected init; }

    /// <summary>Initializes a new entity with the given identifier.</summary>
    /// <param name="id">The entity's unique identifier.</param>
    protected Entity(Guid id)
    {
        Id = id;
    }

    /// <inheritdoc />
    public bool Equals(Entity? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id == other.Id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Entity);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>Compares two entities for identity equality.</summary>
    public static bool operator ==(Entity? left, Entity? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Compares two entities for identity inequality.</summary>
    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
