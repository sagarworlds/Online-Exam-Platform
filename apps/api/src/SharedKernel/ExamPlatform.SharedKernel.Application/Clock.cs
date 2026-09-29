namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// Injectable source of the current UTC time. Every place that would otherwise
/// call <c>DateTime.UtcNow</c> directly depends on this instead, so tests can
/// substitute a fake clock to exercise expiry, timeout, and clock-skew logic
/// deterministically.
/// </summary>
public interface Clock
{
    /// <summary>The current instant, in UTC.</summary>
    DateTime UtcNow { get; }
}
