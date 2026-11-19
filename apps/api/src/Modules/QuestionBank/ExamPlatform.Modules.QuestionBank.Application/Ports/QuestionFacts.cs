using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>What the bank keeps about a question in plain columns, so it can be read without loading or decrypting any of the question's content.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="ChapterId">The chapter the question is filed under, or null when it is not filed.</param>
/// <param name="Status">Where the question stands in review (FR-8).</param>
/// <param name="CurrentVersion">The number of the version in force, or null when none was ever stored (a question from before versions were kept).</param>
public sealed record QuestionFacts(Guid Id, Guid? ChapterId, QuestionStatus Status, int? CurrentVersion);
