namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>How often one option was chosen.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Text">The option text as it is now.</param>
/// <param name="IsCorrect">Whether it is a correct option now.</param>
/// <param name="TimesChosen">How many answers chose it.</param>
public sealed record OptionStatisticsDto(Guid Id, string Text, bool IsCorrect, int TimesChosen);

/// <summary>Where a question is used and how candidates have fared on it (FR-9).</summary>
/// <param name="ExamCount">How many exams contain the question.</param>
/// <param name="ExamNames">The names of some of those exams; <see cref="ExamCount"/> is the full number.</param>
/// <param name="Answered">In how many finished, valid attempts it was answered.</param>
/// <param name="Correct">How many of those answers were fully correct.</param>
/// <param name="PercentCorrect">The share of answers that were fully correct, 0 to 100 with one decimal, or null when nobody has answered it yet.</param>
/// <param name="Options">The question's options as they are now, each with how often it was chosen.</param>
public sealed record QuestionStatisticsDto(
    int ExamCount, IReadOnlyList<string> ExamNames, int Answered, int Correct, decimal? PercentCorrect, IReadOnlyList<OptionStatisticsDto> Options);
