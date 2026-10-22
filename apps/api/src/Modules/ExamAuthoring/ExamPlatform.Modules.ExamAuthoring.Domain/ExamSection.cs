using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.Domain;

public class ExamSection
{
    public Guid Id { get; set; }
    public Guid ExamId { get; set; }
    public string Name { get; set; } = null!;
    public int? TimeSeconds { get; set; }
    public int Order { get; set; }
    public bool IsDeleted { get; set; }
    private readonly List<ExamQuestion> _questions = [];
    public IReadOnlyList<ExamQuestion> Questions => _questions.AsReadOnly();
    private readonly List<SectionDrawRule> _drawRules = [];

    /// <summary>The rules that add questions drawn at random for each candidate, on top of <see cref="Questions"/>.</summary>
    public IReadOnlyList<SectionDrawRule> DrawRules => _drawRules.AsReadOnly();
    public DateTime CreatedAt { get; set; }

    private ExamSection() { }

    public ExamSection(Guid examId, string name, int? timeSeconds, int order, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        ExamId = examId;
        Name = name;
        TimeSeconds = timeSeconds;
        Order = order;
        CreatedAt = nowUtc;
    }

    public ExamQuestion AddQuestion(Guid questionVersionId, int order, DateTime nowUtc)
    {
        var exists = _questions.Any(q => q.QuestionVersionId == questionVersionId);
        if (exists)
            throw new DuplicateQuestionError(questionVersionId, Id);

        var question = new ExamQuestion(Id, questionVersionId, order, nowUtc);
        _questions.Add(question);
        return question;
    }

    /// <summary>The most draw rules one section may have.</summary>
    public const int MaxDrawRules = 20;

    /// <summary>Adds a rule that draws questions for each candidate.</summary>
    /// <param name="count">How many to draw.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="bookId">Only questions of this book, or null for any.</param>
    /// <param name="chapterId">Only questions of this chapter, or null for any.</param>
    /// <param name="difficulty">Only this difficulty, or blank for any.</param>
    /// <param name="topic">Only this topic, or blank for any.</param>
    /// <returns>The rule as added.</returns>
    /// <exception cref="InvalidExamConfigError">A part of the rule is invalid, or the section already has the most rules it may.</exception>
    public SectionDrawRule AddDrawRule(int count, Guid? bookId, Guid? chapterId, string? difficulty, string? topic, DateTime nowUtc)
    {
        if (_drawRules.Count >= MaxDrawRules)
            throw new InvalidExamConfigError($"A section can have at most {MaxDrawRules} draw rules.");

        var (book, chapter, level, cleanTopic) = SectionDrawRule.Clean(count, bookId, chapterId, difficulty, topic);
        var rule = new SectionDrawRule(Id, _drawRules.Count + 1, count, book, chapter, level, cleanTopic, nowUtc);
        _drawRules.Add(rule);
        return rule;
    }

    /// <summary>Takes a draw rule out of the section and closes the gap it leaves in the numbering.</summary>
    /// <param name="ruleId">The rule's id.</param>
    /// <returns><see langword="true"/> when the rule was here; <see langword="false"/> when it was not.</returns>
    public bool RemoveDrawRule(Guid ruleId)
    {
        var rule = _drawRules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
            return false;

        _drawRules.Remove(rule);
        var order = 1;
        foreach (var remaining in _drawRules.OrderBy(r => r.Order))
            remaining.Order = order++;

        return true;
    }

    /// <summary>Puts the section's questions in the order given and numbers them 1, 2, 3 ... in it.</summary>
    /// <param name="orderedQuestionVersionIds">Every question's id in the question bank, once each, in the order wanted.</param>
    /// <returns>
    /// <see langword="true"/> when the list named exactly the section's questions; <see langword="false"/> when it did not, in which case nothing
    /// was changed, so the caller can say so.
    /// </returns>
    public bool ReorderQuestions(IReadOnlyList<Guid>? orderedQuestionVersionIds)
    {
        if (orderedQuestionVersionIds is null || orderedQuestionVersionIds.Count != _questions.Count)
            return false;

        var held = _questions.Select(q => q.QuestionVersionId).ToHashSet();
        if (!orderedQuestionVersionIds.ToHashSet().SetEquals(held))
            return false;

        for (var place = 0; place < orderedQuestionVersionIds.Count; place++)
            _questions.First(q => q.QuestionVersionId == orderedQuestionVersionIds[place]).Order = place + 1;

        return true;
    }

    /// <summary>Takes a question out of the section and closes the gap it leaves in the numbering.</summary>
    /// <param name="questionVersionId">The question's id in the question bank.</param>
    /// <returns><see langword="true"/> when the question was here; <see langword="false"/> when it was not, so the caller can say so.</returns>
    public bool RemoveQuestion(Guid questionVersionId)
    {
        var question = _questions.FirstOrDefault(q => q.QuestionVersionId == questionVersionId);
        if (question is null)
            return false;

        _questions.Remove(question);

        // Candidates see the questions in this order, so it must stay 1, 2, 3 ... with no hole where one was removed.
        var order = 1;
        foreach (var remaining in _questions.OrderBy(q => q.Order))
            remaining.Order = order++;

        return true;
    }
}
