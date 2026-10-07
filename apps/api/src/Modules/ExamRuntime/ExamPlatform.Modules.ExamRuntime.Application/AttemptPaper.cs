using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>Swaps an exam's question lists for the paper drawn for one attempt, and applies the candidate's accommodation, so everything downstream just reads the exam.</summary>
public static class AttemptPaper
{
    /// <summary>The exam as this attempt sees it.</summary>
    /// <param name="exam">The exam as authored.</param>
    /// <param name="attempt">The attempt; one with no stored paper sits the exam's fixed questions.</param>
    public static ExamSnapshot For(this ExamSnapshot exam, Attempt attempt)
    {
        // What the candidate's accommodation changes about the exam (FR-49) is applied here, so every handler that reads the attempt's exam
        // sees the same rules the candidate was told.
        exam = AccommodationPolicy.Apply(exam, attempt.AccommodationFormats);

        if (attempt.Paper.Count == 0)
            return exam;

        return exam with
        {
            Sections = exam.Sections
                .Select(s => s with
                {
                    QuestionIds = attempt.Paper.Where(q => q.SectionId == s.Id).OrderBy(q => q.Order).Select(q => q.QuestionId).ToList(),
                    DrawRules = null,
                })
                .ToList(),
        };
    }
}
