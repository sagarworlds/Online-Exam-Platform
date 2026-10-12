using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// Value equality as the shared base defines it: two value objects are equal when they are the same type and their
/// components match, never by identity. Money, Label and Alias are test-only value objects.
/// </summary>
public class ValueObjectTests
{
    private sealed class Money(decimal amount, string currency) : ValueObject
    {
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return amount;
            yield return currency;
        }
    }

    private sealed class Label(string text) : ValueObject
    {
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return text;
        }
    }

    // Same shape as Label, so a test can show that equal components are not enough when the types differ.
    private sealed class Alias(string text) : ValueObject
    {
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return text;
        }
    }

    [Fact]
    public void ComponentsMatch_AreEqual_ByMethodAndByOperator()
    {
        var first = new Money(5, "INR");
        var second = new Money(5, "INR");

        Assert.True(first.Equals(second));
        Assert.True(first == second);
        Assert.False(first != second);
    }

    [Fact]
    public void EqualValues_HaveTheSameHashCode()
    {
        Assert.Equal(new Money(5, "INR").GetHashCode(), new Money(5, "INR").GetHashCode());
    }

    [Theory]
    [InlineData(6, "INR")]
    [InlineData(5, "USD")]
    public void AComponentDiffers_IsNotEqual(decimal amount, string currency)
    {
        var original = new Money(5, "INR");
        var other = new Money(amount, currency);

        Assert.False(original.Equals(other));
        Assert.True(original != other);
    }

    [Fact]
    public void EqualComponents_OfDifferentTypes_AreNotEqual()
    {
        Assert.False(new Label("x").Equals(new Alias("x")));
    }

    [Fact]
    public void ANullOrADifferentKindOfObject_IsNotEqual()
    {
        var label = new Label("x");

        Assert.False(label.Equals((ValueObject?)null));
        Assert.False(label.Equals((object?)null));
        Assert.False(label.Equals("x"));
    }

    [Fact]
    public void AValueObject_IsEqualToItself()
    {
        var label = new Label("x");

        Assert.True(label.Equals(label));
    }

    [Fact]
    public void TheOperators_HandleNullOnEitherSide()
    {
        ValueObject? none = null;

        Assert.True(none == null);
        Assert.False(none != null);
        Assert.True(none != new Label("x"));
        Assert.False(none == new Label("x"));
    }
}
