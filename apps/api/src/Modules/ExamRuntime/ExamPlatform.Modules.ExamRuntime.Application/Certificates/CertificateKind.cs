namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Which certificate a released result earns (FR-34). A candidate who reached an exam's pass mark gets an achievement certificate, which shows
/// the score. Everyone else gets a participation certificate, which names no score: an exam with no pass mark has no pass for a score to
/// show, and a candidate who did not reach the mark has not passed, so the score is not printed on their certificate.
/// </summary>
public enum CertificateKind
{
    /// <summary>The candidate reached the exam's pass mark.</summary>
    Achievement,

    /// <summary>The candidate took part, without reaching a pass mark, or the exam has none.</summary>
    Participation,
}
