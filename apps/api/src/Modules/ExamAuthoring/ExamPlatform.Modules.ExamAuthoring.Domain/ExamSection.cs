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
    public DateTime CreatedAt { get; set; }

    private ExamSection() { }

    public ExamSection(Guid examId, string name, int? timeSeconds, int order)
    {
        Id = Guid.NewGuid();
        ExamId = examId;
        Name = name;
        TimeSeconds = timeSeconds;
        Order = order;
        CreatedAt = DateTime.UtcNow;
    }

    public ExamQuestion AddQuestion(Guid questionVersionId, int order)
    {
        var exists = _questions.Any(q => q.QuestionVersionId == questionVersionId);
        if (exists)
            throw new DuplicateQuestionError(questionVersionId, Id);

        var question = new ExamQuestion(Id, questionVersionId, order);
        _questions.Add(question);
        return question;
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
