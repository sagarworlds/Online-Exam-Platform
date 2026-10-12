namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Which certificate a released result earns (FR-34). An exam with a pass mark gives an achievement certificate to a candidate who reached
/// the mark, and it shows the score. An exam with no pass mark gives a participation certificate to every candidate, which names no score,
/// because there is no pass for the score to show.
/// </summary>
public enum CertificateKind
{
    /// <summary>The candidate reached the exam's pass mark.</summary>
    Achievement,

    /// <summary>The exam has no pass mark, so the candidate is certified as having taken part.</summary>
    Participation,
}
