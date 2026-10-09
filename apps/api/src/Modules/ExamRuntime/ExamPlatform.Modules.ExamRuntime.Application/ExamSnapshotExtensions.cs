using ExamPlatform.Modules.ExamAuthoring.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>Questions to ask of an exam, shared by every handler that takes a question id from the client.</summary>
internal static class ExamSnapshotExtensions
{
    /// <summary>
    /// Whether a question is part of the exam. A question id comes from the client and is never trusted: without this check an
    /// answer or a mark could be filed against any question in the bank.
    /// </summary>
    /// <param name="exam">The exam.</param>
    /// <param name="questionId">The question-bank id to look for.</param>
    public static bool Includes(this ExamSnapshot exam, Guid questionId) =>
        exam.Sections.Any(s => s.QuestionIds.Contains(questionId));
}
