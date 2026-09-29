using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>Settable <see cref="Clock"/> for deterministic expiry/skew tests.</summary>
public sealed class FakeClock(DateTime initialUtcNow) : Clock
{
    /// <inheritdoc />
    public DateTime UtcNow { get; set; } = initialUtcNow;
}
