using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>
/// A candidate has answered the question, so its options and answer key can no longer change (FR-7): every stored score
/// and every review was worked out against them. Only the wording can still be corrected.
/// </summary>
public sealed class QuestionLockedError() : DomainException(
    "Candidates have already answered this question, so only its wording can change. " +
    "To change the answers, create a new question instead.")
{
    /// <inheritdoc />
    public override string ErrorCode => "question_locked";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
