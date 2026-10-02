using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>
/// The book, or the chapter, is archived: it is kept for the questions already filed under it, but nothing new can be
/// added to it until it is restored.
/// </summary>
/// <param name="message">Says what is archived and what can be done about it.</param>
public sealed class BookArchivedError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "book_archived";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
