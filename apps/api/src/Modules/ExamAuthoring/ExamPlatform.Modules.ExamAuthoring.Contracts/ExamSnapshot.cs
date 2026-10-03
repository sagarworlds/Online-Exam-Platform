namespace ExamPlatform.Modules.ExamAuthoring.Contracts;

/// <summary>An exam as another module sees it.</summary>
/// <param name="Id">The exam's id.</param>
/// <param name="Name">The exam's name.</param>
/// <param name="Description">An optional description for candidates.</param>
/// <param name="IsPublished">Whether candidates may take it.</param>
/// <param name="StartUtc">When the window opens; only meaningful once the exam is scheduled.</param>
/// <param name="EndUtc">When the window closes; every attempt ends by then.</param>
/// <param name="LateEntryDeadlineUtc">The last moment a candidate may still start, if limited.</param>
/// <param name="DurationSeconds">How long one attempt lasts, or null for "until the window closes".</param>
/// <param name="CorrectMarks">Marks for a correct answer.</param>
/// <param name="IncorrectMarks">Marks for a wrong answer (zero or negative).</param>
/// <param name="UnattemptedMarks">Marks for an unanswered question.</param>
/// <param name="Sections">The sections in order, each with its question ids in order.</param>
/// <param name="ResultRelease">When candidates may see which answers were right; Instant unless the author chose otherwise.</param>
/// <param name="ResultReleaseTimeUtc">
/// From when the answers are visible, for Scheduled, and for Manual once an administrator has released them; null otherwise.
/// </param>
/// <param name="SectionLockEnabled">Whether a candidate who leaves a section may not come back to it (FR-12 "section lock").</param>
/// <param name="MaxAttempts">
/// How many attempts every enrolled candidate has before an administrator gives anyone an extra one; 1 unless the author chose more.
/// </param>
/// <param name="ShuffleQuestions">Whether the author asked for the questions within each section to be shuffled on every attempt, the first included.</param>
/// <param name="ShuffleOptions">Whether the author asked for the options of each question to be shuffled on every attempt, the first included.</param>
public sealed record ExamSnapshot(
    Guid Id,
    string Name,
    string? Description,
    bool IsPublished,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime? LateEntryDeadlineUtc,
    int? DurationSeconds,
    decimal CorrectMarks,
    decimal IncorrectMarks,
    decimal UnattemptedMarks,
    IReadOnlyList<ExamSectionSnapshot> Sections,
    ExamResultReleaseMode ResultRelease = ExamResultReleaseMode.Instant,
    DateTime? ResultReleaseTimeUtc = null,
    bool SectionLockEnabled = false,
    int MaxAttempts = 1,
    bool ShuffleQuestions = false,
    bool ShuffleOptions = false);

/// <summary>One section of an <see cref="ExamSnapshot"/>.</summary>
/// <param name="Id">The section's id.</param>
/// <param name="Name">The section's name.</param>
/// <param name="Order">Position in the exam, from 1.</param>
/// <param name="QuestionIds">The question-bank ids of the section's questions, in order.</param>
/// <param name="DrawRules">
/// Rules that add questions drawn at random for each candidate on top of <paramref name="QuestionIds"/>; empty for a section whose
/// questions are all fixed.
/// </param>
public sealed record ExamSectionSnapshot(
    Guid Id, string Name, int Order, IReadOnlyList<Guid> QuestionIds, IReadOnlyList<DrawRuleSnapshot>? DrawRules = null);

/// <summary>
/// A rule that draws questions for a candidate's paper, with the exam's scope already folded into where it may draw from, so the
/// module that draws needs no knowledge of scopes.
/// </summary>
/// <param name="Count">How many questions to draw.</param>
/// <param name="BookId">Only questions of this book, or null for any.</param>
/// <param name="ChapterId">Only questions of this chapter, or null for any.</param>
/// <param name="ChapterIds">Only questions of one of these chapters, or null for any; set when the exam is limited to chosen chapters.</param>
/// <param name="Difficulty">Only this difficulty ("easy", "medium", "hard"), or null for any.</param>
/// <param name="Topic">Only questions with this topic, or null for any.</param>
public sealed record DrawRuleSnapshot(
    int Count, Guid? BookId, Guid? ChapterId, IReadOnlyList<Guid>? ChapterIds, string? Difficulty, string? Topic);
