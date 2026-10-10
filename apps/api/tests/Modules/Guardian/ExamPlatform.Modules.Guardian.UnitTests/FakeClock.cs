using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Guardian.UnitTests;

/// <summary>Settable <see cref="Clock"/> for deterministic tests of link expiry.</summary>
public sealed class FakeClock(DateTime initialUtcNow) : Clock
{
    /// <inheritdoc />
    public DateTime UtcNow { get; set; } = initialUtcNow;
}
