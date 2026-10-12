using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// Identity equality as the shared base defines it: two entities are the same when they have the same type and id,
/// whatever their other properties hold. Seat and Ticket are test-only entities.
/// </summary>
public class EntityTests
{
    private sealed class Seat(Guid id) : Entity(id) { }

    private sealed class Ticket(Guid id) : Entity(id) { }

    [Fact]
    public void SameTypeAndId_AreEqual_ByMethodAndByOperator()
    {
        var id = Guid.NewGuid();

        Assert.True(new Seat(id).Equals(new Seat(id)));
        Assert.True(new Seat(id) == new Seat(id));
        Assert.False(new Seat(id) != new Seat(id));
    }

    [Fact]
    public void SameTypeAndId_HaveTheSameHashCode()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new Seat(id).GetHashCode(), new Seat(id).GetHashCode());
    }

    [Fact]
    public void DifferentIds_AreNotEqual()
    {
        Assert.False(new Seat(Guid.NewGuid()).Equals(new Seat(Guid.NewGuid())));
    }

    [Fact]
    public void SameIdOfDifferentTypes_AreNotEqual()
    {
        var id = Guid.NewGuid();

        Assert.False(new Seat(id).Equals(new Ticket(id)));
    }

    [Fact]
    public void ANullOrADifferentKindOfObject_IsNotEqual()
    {
        var seat = new Seat(Guid.NewGuid());

        Assert.False(seat.Equals((Entity?)null));
        Assert.False(seat.Equals((object?)null));
        Assert.False(seat.Equals("seat"));
    }

    [Fact]
    public void TheOperators_HandleNullOnEitherSide()
    {
        Entity? none = null;
        var seat = new Seat(Guid.NewGuid());

        Assert.True(none == null);
        Assert.False(none != null);
        Assert.True(none != seat);
        Assert.False(none == seat);
    }
}
