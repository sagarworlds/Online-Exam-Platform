using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Admin.UnitTests;

/// <summary>Settable <see cref="Clock"/> for deterministic tests.</summary>
public sealed class FakeClock(DateTime initialUtcNow) : Clock
{
    /// <inheritdoc />
    public DateTime UtcNow { get; set; } = initialUtcNow;
}
