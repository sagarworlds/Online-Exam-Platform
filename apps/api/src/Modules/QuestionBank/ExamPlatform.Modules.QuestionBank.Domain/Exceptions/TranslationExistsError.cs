using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>The question already has a linked translation in that language (FR-10).</summary>
/// <param name="language">The language that is already taken.</param>
public sealed class TranslationExistsError(string language)
    : DomainException($"This question already has a translation in '{language}'.")
{
    /// <inheritdoc />
    public override string ErrorCode => "translation_exists";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
