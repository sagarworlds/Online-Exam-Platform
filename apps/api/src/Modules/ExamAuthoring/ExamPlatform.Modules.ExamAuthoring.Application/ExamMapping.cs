using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>Maps <see cref="Exam"/> to its DTOs, so every handler reports an exam the same way.</summary>
internal static class ExamMapping
{
    /// <summary>Maps an exam without its sections (for listings and for changes that do not touch them).</summary>
    /// <param name="exam">The exam to map.</param>
    public static ExamDto ToDto(this Exam exam) =>
        new(
            exam.Id,
            exam.SeriesId,
            exam.Name,
            exam.Description,
            exam.Status,
            new ExamConfigDto(
                exam.Config.TotalTimeSeconds,
                exam.Config.ShuffleQuestions,
                exam.Config.ShuffleOptions,
                exam.Config.SectionLockEnabled,
                exam.Config.CalculatorAllowed,
                exam.Config.ScratchpadAllowed,
                exam.Config.MaxAttempts,
                exam.Config.MaxRetakes,
                exam.Config.ResultReleaseMode,
                exam.Config.ResultReleaseTime,
                exam.Config.MarkingScheme,
                exam.Config.ContentProtection,
                exam.Config.FocusViolationLimit),
            exam.ScheduledStartTime,
            exam.ScheduledEndTime,
            exam.LateEntryDeadline,
            exam.TimeZone,
            exam.CreatedBy,
            exam.CreatedAt,
            exam.UpdatedAt,
            exam.IsScheduled,
            Scope: new ExamScopeDto(
                exam.Scope.Type,
                exam.Scope.BookId,
                BookName: null,
                exam.Scope.ChapterIds.Select(id => new ExamScopeChapterDto(id, Title: null)).ToList()));

    /// <summary>Maps an exam together with its sections and questions.</summary>
    /// <param name="exam">The exam to map.</param>
    /// <param name="questionTexts">Question text by bank id; a question the bank no longer has maps with null text.</param>
    public static ExamDto ToDetailDto(this Exam exam, IReadOnlyDictionary<Guid, string> questionTexts) =>
        exam.ToDto() with
        {
            Sections = exam.Sections
                .OrderBy(s => s.Order)
                .Select(s => s.ToDto(questionTexts))
                .ToList(),
        };

    /// <summary>Maps one section and its questions.</summary>
    /// <param name="section">The section to map.</param>
    /// <param name="questionTexts">Question text by bank id.</param>
    public static ExamSectionDto ToDto(this ExamSection section, IReadOnlyDictionary<Guid, string> questionTexts) =>
        new(
            section.Id,
            section.Name,
            section.TimeSeconds,
            section.Order,
            section.Questions
                .OrderBy(q => q.Order)
                .Select(q => new ExamQuestionDto(
                    q.Id,
                    q.QuestionVersionId,
                    q.Order,
                    questionTexts.GetValueOrDefault(q.QuestionVersionId)))
                .ToList(),
            section.DrawRules.OrderBy(r => r.Order).Select(r => r.ToDto()).ToList());

    /// <summary>Maps one draw rule.</summary>
    /// <param name="rule">The rule to map.</param>
    public static DrawRuleDto ToDto(this SectionDrawRule rule) =>
        new(rule.Id, rule.Order, rule.Count, rule.BookId, rule.ChapterId, rule.Difficulty, rule.Topic);
}
