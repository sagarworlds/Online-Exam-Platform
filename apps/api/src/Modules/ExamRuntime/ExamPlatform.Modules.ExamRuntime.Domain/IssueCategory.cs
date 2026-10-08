namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>What kind of problem a candidate reports from inside an exam (FR-42), so staff can sort the queue and know who should look.</summary>
public enum IssueCategory
{
    /// <summary>Something is wrong with a question: it is unclear, shows wrongly, or its options do not make sense.</summary>
    Question,

    /// <summary>The exam page or the candidate's connection or device is not working.</summary>
    Technical,

    /// <summary>Anything else.</summary>
    Other,
}
