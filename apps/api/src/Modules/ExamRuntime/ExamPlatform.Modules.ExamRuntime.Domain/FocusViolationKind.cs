namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>How a candidate left the exam page (FR-22).</summary>
public enum FocusViolationKind
{
    /// <summary>The exam's browser tab was hidden: the candidate switched tabs or minimised the window.</summary>
    TabHidden,

    /// <summary>The page lost focus without being hidden: the candidate moved to another window or application.</summary>
    WindowBlurred,

    /// <summary>The candidate left full-screen mode.</summary>
    FullscreenExited,
}
