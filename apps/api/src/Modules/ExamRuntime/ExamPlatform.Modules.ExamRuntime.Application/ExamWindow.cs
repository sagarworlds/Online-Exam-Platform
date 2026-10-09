using ExamPlatform.Modules.ExamAuthoring.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>The timing rules of an exam's window (FR-13), kept in one place so every screen and every attempt agrees on them.</summary>
public static class ExamWindow
{
    /// <summary>The last moment a new attempt may start: the late-entry cutoff when there is one, otherwise the window's end.</summary>
    /// <param name="exam">The exam.</param>
    public static DateTime LastStartUtc(ExamSnapshot exam) =>
        exam.LateEntryDeadlineUtc is { } cutoff && cutoff < exam.EndUtc ? cutoff : exam.EndUtc;

    /// <summary>Whether a new attempt may start at <paramref name="nowUtc"/>.</summary>
    /// <param name="exam">The exam.</param>
    /// <param name="nowUtc">The current instant.</param>
    public static bool CanStart(ExamSnapshot exam, DateTime nowUtc) => nowUtc >= exam.StartUtc && nowUtc <= LastStartUtc(exam);

    /// <summary>
    /// When an attempt that starts at <paramref name="startedAtUtc"/> must end: its duration after it started, but
    /// never later than the window's end (a late starter does not get extra time).
    /// </summary>
    /// <param name="exam">The exam.</param>
    /// <param name="startedAtUtc">When the attempt started.</param>
    public static DateTime DeadlineUtc(ExamSnapshot exam, DateTime startedAtUtc)
    {
        var byDuration = exam.DurationSeconds is { } seconds ? startedAtUtc.AddSeconds(seconds) : exam.EndUtc;
        return byDuration < exam.EndUtc ? byDuration : exam.EndUtc;
    }
}
