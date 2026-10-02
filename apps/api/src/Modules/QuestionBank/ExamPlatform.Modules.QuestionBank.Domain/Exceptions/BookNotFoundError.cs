using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>No book has the requested id.</summary>
public sealed class BookNotFoundError() : DomainException("No book matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "book_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
