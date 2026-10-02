namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>Where a question is in use, so the authoring screen can say why it cannot be deleted or why its answers are locked.</summary>
/// <param name="ExamCount">How many exams contain the question.</param>
/// <param name="ExamNames">The names of some of those exams, at most <see cref="MaxNamedExams"/>, for the reason shown to the author.</param>
/// <param name="Answered">Whether any candidate has saved an answer to the question.</param>
public sealed record QuestionUsageDto(int ExamCount, IReadOnlyList<string> ExamNames, bool Answered)
{
    /// <summary>How many exam names are listed at most; <see cref="ExamCount"/> is always the full number.</summary>
    public const int MaxNamedExams = 5;

    /// <summary>A question that nothing uses.</summary>
    public static QuestionUsageDto Unused { get; } = new(0, [], false);

    /// <summary>Whether any exam contains the question.</summary>
    public bool IsInAnyExam => ExamCount > 0;
}
