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

    public void RemoveQuestion(Guid questionVersionId)
    {
        var question = _questions.FirstOrDefault(q => q.QuestionVersionId == questionVersionId);
        if (question != null)
            _questions.Remove(question);
    }
}
