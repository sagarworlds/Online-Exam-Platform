using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>The bank already holds a question with the same wording and the same options (FR-9).</summary>
public sealed class DuplicateQuestionError()
    : DomainException("The question bank already has a question with this wording and these options. Add it anyway if you mean to.")
{
    /// <inheritdoc />
    public override string ErrorCode => "duplicate_question";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
