using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>An answer option as a candidate sees it. Deliberately has no "is correct" flag.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Text">The option text.</param>
public sealed record AttemptOptionDto(Guid Id, string Text);

/// <summary>A question as a candidate sees it, with the answer they have saved so far.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Text">The question text, as sanitized HTML; render it with an HTML sanitizer in place, never as trusted markup.</param>
/// <param name="Options">The options, in display order.</param>
/// <param name="SelectedOptionId">The option the candidate chose, or null when unanswered.</param>
/// <param name="MarkedForReview">Whether the candidate has marked the question to come back to. It has no effect on the score.</param>
public sealed record AttemptQuestionDto(
    Guid Id,
    string Text,
    IReadOnlyList<AttemptOptionDto> Options,
    Guid? SelectedOptionId,
    bool MarkedForReview = false);

/// <summary>A section of the exam as a candidate sees it.</summary>
/// <param name="Id">The section's id.</param>
/// <param name="Name">The section's name.</param>
/// <param name="Questions">The questions, in order.</param>
public sealed record AttemptSectionDto(Guid Id, string Name, IReadOnlyList<AttemptQuestionDto> Questions);

/// <summary>An attempt: what the candidate needs to sit the exam, or to read their result.</summary>
/// <param name="Id">The attempt's id.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="Status">Whether the attempt is still open.</param>
/// <param name="StartedAtUtc">When the candidate started.</param>
/// <param name="DeadlineUtc">When the server will close the attempt.</param>
/// <param name="SubmittedAtUtc">When it ended, once submitted.</param>
/// <param name="AutoSubmitted">Whether it ended because time ran out.</param>
/// <param name="Score">The marks scored, once submitted.</param>
/// <param name="MaxScore">The marks available, once submitted.</param>
/// <param name="ServerTimeUtc">The server's clock when this was produced, so a client can show a countdown that ignores its own clock's error.</param>
/// <param name="Sections">The questions, while the attempt is open; empty once it is submitted.</param>
/// <param name="Review">Whether the answers can be reviewed, once the attempt is submitted; null while it is open.</param>
/// <param name="Number">Which attempt this is for the candidate at the exam, from 1.</param>
public sealed record AttemptDto(
    Guid Id,
    Guid ExamId,
    string ExamName,
    AttemptStatus Status,
    DateTime StartedAtUtc,
    DateTime DeadlineUtc,
    DateTime? SubmittedAtUtc,
    bool AutoSubmitted,
    decimal? Score,
    decimal? MaxScore,
    DateTime ServerTimeUtc,
    IReadOnlyList<AttemptSectionDto> Sections,
    AttemptReviewAvailabilityDto? Review = null,
    int Number = 1);
