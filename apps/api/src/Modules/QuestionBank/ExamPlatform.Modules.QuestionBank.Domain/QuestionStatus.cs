namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>
/// Where a question is in the review workflow (FR-8): <c>draft → review → approved → retired</c>. Stored by name, so the column
/// reads the same in a query and survives a reordered enum.
/// </summary>
public enum QuestionStatus
{
    /// <summary>Being written or revised; not yet put forward for review.</summary>
    Draft = 1,

    /// <summary>Put forward by its author and waiting for a reviewer.</summary>
    InReview = 2,

    /// <summary>A reviewer has approved it.</summary>
    Approved = 3,

    /// <summary>Taken out of use: it cannot be added to an exam or drawn into a paper, but exams and attempts that already hold it still read it.</summary>
    Retired = 4,
}
