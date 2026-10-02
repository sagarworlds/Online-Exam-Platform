using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>A question is not in the book or chapters its exam is limited to (FR-11).</summary>
public sealed class QuestionOutsideExamScopeError : DomainException
{
    /// <summary>The questions that fall outside the scope.</summary>
    public IReadOnlyList<Guid> QuestionIds { get; }

    /// <summary>Reports a single question that does not belong in the exam.</summary>
    /// <param name="questionId">The question.</param>
    public QuestionOutsideExamScopeError(Guid questionId)
        : base("This question is not in the book or chapters this exam is limited to. Choose a question from them, or change the exam's scope.")
    {
        QuestionIds = [questionId];
    }

    /// <summary>Reports the questions an exam already holds that a new scope would leave outside it.</summary>
    /// <param name="questionIds">The questions that would be outside the new scope.</param>
    public QuestionOutsideExamScopeError(IReadOnlyList<Guid> questionIds)
        : base($"{questionIds.Count} question(s) already in this exam are outside that scope. Remove them first, or choose a wider scope.")
    {
        QuestionIds = questionIds;
    }

    /// <inheritdoc />
    public override string ErrorCode => "question_outside_scope";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
