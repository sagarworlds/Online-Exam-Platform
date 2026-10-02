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
    IReadOnlyList<ExamSectionSnapshot> Sections);

/// <summary>One section of an <see cref="ExamSnapshot"/>.</summary>
/// <param name="Id">The section's id.</param>
/// <param name="Name">The section's name.</param>
/// <param name="Order">Position in the exam, from 1.</param>
/// <param name="QuestionIds">The question-bank ids of the section's questions, in order.</param>
public sealed record ExamSectionSnapshot(Guid Id, string Name, int Order, IReadOnlyList<Guid> QuestionIds);
