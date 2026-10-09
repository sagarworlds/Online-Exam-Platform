using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>No chapter has the requested id (or the book has no such chapter).</summary>
public sealed class ChapterNotFoundError() : DomainException("No chapter matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "chapter_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
