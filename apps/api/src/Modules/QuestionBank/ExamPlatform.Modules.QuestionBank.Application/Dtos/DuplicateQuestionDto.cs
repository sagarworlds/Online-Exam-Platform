namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>An existing question that repeats one being added (FR-9).</summary>
/// <param name="Id">The existing question's id.</param>
/// <param name="Preview">The start of its wording as plain text, enough to recognise it.</param>
/// <param name="SameOptions">Whether it has the same options too, which is what makes it a duplicate; false means only the wording matches.</param>
/// <param name="Status">Its review status.</param>
/// <param name="ChapterId">The chapter it is filed under, or null.</param>
public sealed record DuplicateQuestionDto(Guid Id, string Preview, bool SameOptions, string Status, Guid? ChapterId)
{
    /// <summary>How many characters of the wording the preview keeps.</summary>
    public const int PreviewLength = 160;
}
