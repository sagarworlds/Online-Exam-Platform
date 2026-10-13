using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>
/// A clock that always reads one instant, so the handler tests that stamp times are deterministic. Handlers take the clock as a
/// dependency, which is the only way their times can be fixed, since the domain itself never reads the system clock.
/// </summary>
internal static class TestClock
{
    /// <summary>A clock whose <see cref="Clock.UtcNow"/> is always <paramref name="instant"/>.</summary>
    /// <param name="instant">The instant to read.</param>
    /// <returns>The clock.</returns>
    public static Clock At(DateTime instant)
    {
        var clock = Substitute.For<Clock>();
        clock.UtcNow.Returns(instant);
        return clock;
    }
}
