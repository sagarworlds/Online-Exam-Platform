using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>A book already has a chapter with that title.</summary>
/// <param name="title">The title that is taken.</param>
public sealed class DuplicateChapterError(string title) : DomainException($"This book already has a chapter called \"{title}\".")
{
    /// <inheritdoc />
    public override string ErrorCode => "duplicate_chapter";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
