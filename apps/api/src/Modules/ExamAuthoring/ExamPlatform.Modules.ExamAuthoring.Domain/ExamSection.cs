namespace ExamPlatform.Modules.ExamAuthoring.Domain;

public class ExamSection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public int? TimeSeconds { get; set; }
    public int Order { get; set; }
    private readonly List<ExamQuestion> _questions = [];
    public IReadOnlyList<ExamQuestion> Questions => _questions.AsReadOnly();
    public DateTime CreatedAt { get; set; }

    private ExamSection() { }

    public ExamSection(string name, int? timeSeconds, int order)
    {
        Id = Guid.NewGuid();
        Name = name;
        TimeSeconds = timeSeconds;
        Order = order;
        CreatedAt = DateTime.UtcNow;
    }

    public void AddQuestion(Guid questionVersionId, int order)
    {
        var exists = _questions.Any(q => q.QuestionVersionId == questionVersionId);
        if (exists)
            throw new DuplicateQuestionError(questionVersionId, Id);

        _questions.Add(new ExamQuestion(questionVersionId, order));
    }

    public void RemoveQuestion(Guid questionVersionId)
    {
        var question = _questions.FirstOrDefault(q => q.QuestionVersionId == questionVersionId);
        if (question != null)
            _questions.Remove(question);
    }
}
