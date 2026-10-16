using ExamPlatform.SharedKernel.Application.LoadTest;

namespace ExamPlatform.SharedKernel.UnitTests.LoadTest;

/// <summary>
/// The load test's candidate pool addresses (ADR 0006): numbered from 1, on a domain that cannot exist, so a pool account is never a
/// real person and both commands that build the pool agree on who is in it.
/// </summary>
public class LoadTestAccountPatternTests
{
    private static LoadTestAccountPattern Valid(string pattern = "candidate-{n}@load.invalid", int count = 3)
    {
        Assert.True(LoadTestAccountPattern.TryCreate(pattern, count, out var problem, out var result), problem);
        return result;
    }

    [Fact]
    public void Addresses_AreNumberedFromOne_InOrder()
    {
        var pattern = Valid(count: 3);

        Assert.Equal(["candidate-1@load.invalid", "candidate-2@load.invalid", "candidate-3@load.invalid"], pattern.Addresses());
        Assert.Equal(3, pattern.Count);
    }

    [Fact]
    public void AddressOf_ReplacesThePlaceholderOnce_WhateverTheNumber()
    {
        Assert.Equal("candidate-20000@load.invalid", Valid(count: 20000).AddressOf(20000));
    }

    [Fact]
    public void TryCreate_TrimsThePattern()
    {
        Assert.Equal("a-1@x.invalid", Valid("  a-{n}@x.invalid  ", 1).AddressOf(1));
    }

    [Theory]
    [InlineData("candidate@load.invalid", "exactly once")]
    [InlineData("candidate-{n}-{n}@load.invalid", "exactly once")]
    [InlineData("", "missing")]
    [InlineData("   ", "missing")]
    [InlineData(null, "missing")]
    public void TryCreate_RefusesAPatternWithoutExactlyOnePlaceholder(string? pattern, string reason)
    {
        Assert.False(LoadTestAccountPattern.TryCreate(pattern, 5, out var problem, out _));
        Assert.Contains(reason, problem);
    }

    [Theory]
    [InlineData("candidate-{n}@example.com")]
    [InlineData("candidate-{n}@load.invalid.example.org")]
    [InlineData("candidate-{n}@invalid")]
    public void TryCreate_RefusesAnAddressThatCouldBeARealPerson(string pattern)
    {
        Assert.False(LoadTestAccountPattern.TryCreate(pattern, 5, out var problem, out _));
        Assert.Contains(".invalid", problem);
    }

    [Fact]
    public void TryCreate_AcceptsTheReservedDomainInAnyCase()
    {
        Assert.True(LoadTestAccountPattern.TryCreate("c-{n}@Load.INVALID", 1, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(LoadTestAccountPattern.MaxCount + 1)]
    public void TryCreate_RefusesACountOutsideTheRange(int count)
    {
        Assert.False(LoadTestAccountPattern.TryCreate("c-{n}@load.invalid", count, out var problem, out _));
        Assert.Contains("count", problem);
    }

    [Fact]
    public void TryCreate_AcceptsTheLargestPool()
    {
        Assert.True(LoadTestAccountPattern.TryCreate("c-{n}@load.invalid", LoadTestAccountPattern.MaxCount, out _, out _));
    }
}
