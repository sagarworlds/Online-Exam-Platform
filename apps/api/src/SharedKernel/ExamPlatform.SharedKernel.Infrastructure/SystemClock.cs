using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.SharedKernel.Infrastructure;

/// <summary>Production <see cref="Clock"/> backed by the operating system's real UTC time.</summary>
public sealed class SystemClock : Clock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
