using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>The section-lock rule, kept in one place so every handler that changes a question applies it the same way.</summary>
internal static class SectionLock
{
    /// <summary>
    /// Refuses a change to a question outside the candidate's current section when the exam locks sections. Enforced on the
    /// server because the page's greyed-out palette is a courtesy: a request can name any question in the exam.
    /// </summary>
    /// <param name="exam">The exam.</param>
    /// <param name="attempt">The attempt being changed.</param>
    /// <param name="questionId">The question the candidate is acting on; the caller has already checked it is in the exam.</param>
    /// <exception cref="SectionLockedError">The exam locks sections and the question is in another section.</exception>
    public static void EnsureQuestionReachable(ExamSnapshot exam, Attempt attempt, Guid questionId)
    {
        if (!exam.SectionLockEnabled)
            return;

        var section = exam.Sections.First(s => s.QuestionIds.Contains(questionId));
        if (section.Order != attempt.ActiveSectionOrder)
            throw new SectionLockedError();
    }
}
