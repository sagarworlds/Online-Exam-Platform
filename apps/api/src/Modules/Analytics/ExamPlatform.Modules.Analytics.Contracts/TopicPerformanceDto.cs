namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>How a candidate's answers fell under one topic label, added up across every question that carries it (FR-36).</summary>
/// <param name="Name">The topic label, as the question bank holds it.</param>
/// <param name="QuestionCount">How many questions the candidate sat that carry the label. A question with two labels counts under both.</param>
/// <param name="CorrectCount">Questions answered fully correctly.</param>
/// <param name="WrongCount">Questions answered wrongly.</param>
/// <param name="PartialCount">Multiple-answer questions answered partly right.</param>
/// <param name="UnansweredCount">Questions left unanswered.</param>
/// <param name="Accuracy">The share of answered questions that were fully correct, in percent; null when nothing under the topic was answered.</param>
public sealed record TopicPerformanceDto(
    string Name,
    int QuestionCount,
    int CorrectCount,
    int WrongCount,
    int PartialCount,
    int UnansweredCount,
    decimal? Accuracy);

/// <summary>How long a candidate spent on questions, from the running totals the page reported (FR-36).</summary>
/// <param name="QuestionsTimed">How many questions across the released results have a reported time. A question the page never reported on is not counted.</param>
/// <param name="AverageSeconds">The average time per timed question, in seconds, to one decimal place; null when no question was timed.</param>
/// <param name="Sections">The same figures for each section name, in name order.</param>
public sealed record TimePerQuestionDto(
    int QuestionsTimed,
    decimal? AverageSeconds,
    IReadOnlyList<SectionTimeDto> Sections);

/// <summary>How long a candidate spent on the questions of one section name, across every released result that had it.</summary>
/// <param name="Name">The section's name, as the exam's author wrote it.</param>
/// <param name="QuestionsTimed">How many of its questions have a reported time.</param>
/// <param name="AverageSeconds">The average time per timed question, in seconds, to one decimal place; null when none was timed.</param>
public sealed record SectionTimeDto(string Name, int QuestionsTimed, decimal? AverageSeconds);
