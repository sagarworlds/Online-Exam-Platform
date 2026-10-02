namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>The way another module makes use of a question, which decides what may still be done to the question.</summary>
public enum QuestionUseKind
{
    /// <summary>The question is part of an exam, so exams read its text and options and must not lose it.</summary>
    InExam,

    /// <summary>A candidate has saved an answer to the question, so its options and answer key are part of their result.</summary>
    Answered,
}

/// <summary>One way a question is in use somewhere outside the bank.</summary>
/// <param name="QuestionId">The question that is in use.</param>
/// <param name="Kind">How it is in use.</param>
/// <param name="Description">Words for a person to read, such as the name of the exam that holds the question.</param>
public sealed record QuestionUse(Guid QuestionId, QuestionUseKind Kind, string Description);
