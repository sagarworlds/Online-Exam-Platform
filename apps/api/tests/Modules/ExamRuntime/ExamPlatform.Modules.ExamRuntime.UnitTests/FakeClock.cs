using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

internal sealed class FakeClock(DateTime now) : Clock
{
    public DateTime UtcNow { get; set; } = now;
}
