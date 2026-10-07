namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>
/// Whether a question the bank already holds is refused when it is added again (FR-9). On by default: creating a question that repeats
/// one refuses it unless the author says to add it anyway, and an import leaves repeats out. A deployment that wants the bank to
/// accept repeats without asking sets <c>QuestionBank:RefuseDuplicates</c> to false. Looking for duplicates beforehand is always available.
/// </summary>
/// <param name="Refuse">Whether a repeated question is refused, or left out of an import, unless the caller allows it.</param>
public sealed record QuestionDuplicatePolicy(bool Refuse);
