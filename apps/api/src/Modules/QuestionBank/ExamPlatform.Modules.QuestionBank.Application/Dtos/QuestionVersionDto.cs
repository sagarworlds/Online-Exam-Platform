namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>One version in a question's history (FR-7): its text, options and answer key as they were at the time.</summary>
/// <param name="VersionNumber">1 for the version the question was created with, incrementing by one on every later one.</param>
/// <param name="Text">The question text at the time, as sanitized HTML.</param>
/// <param name="Options">The options as they were, in their display order at the time.</param>
/// <param name="AllowsMultiple">Whether more than one option could be correct at the time.</param>
/// <param name="CreatedAtUtc">When this became the current version.</param>
/// <param name="IsTextAnswer">Whether the candidate typed the answer at the time, rather than choosing an option.</param>
/// <param name="AcceptedAnswers">The accepted answers at the time, for a text question; empty for a multiple-choice one.</param>
public sealed record QuestionVersionDto(
    int VersionNumber,
    string Text,
    IReadOnlyList<QuestionOptionDto> Options,
    bool AllowsMultiple,
    DateTime CreatedAtUtc,
    bool IsTextAnswer = false,
    IReadOnlyList<string>? AcceptedAnswers = null);
