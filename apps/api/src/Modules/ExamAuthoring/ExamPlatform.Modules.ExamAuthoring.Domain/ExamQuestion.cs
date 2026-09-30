namespace ExamPlatform.Modules.ExamAuthoring.Domain;

public class ExamQuestion
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }
    public Guid QuestionVersionId { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; }

    private ExamQuestion() { }

    public ExamQuestion(Guid sectionId, Guid questionVersionId, int order)
    {
        Id = Guid.NewGuid();
        SectionId = sectionId;
        QuestionVersionId = questionVersionId;
        Order = order;
        CreatedAt = DateTime.UtcNow;
    }
}
