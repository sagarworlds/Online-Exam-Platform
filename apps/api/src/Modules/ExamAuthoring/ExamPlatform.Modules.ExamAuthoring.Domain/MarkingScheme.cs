namespace ExamPlatform.Modules.ExamAuthoring.Domain;

public record MarkingScheme(decimal CorrectMarks, decimal IncorrectMarks, decimal UnattemptedMarks)
{
    public MarkingScheme() : this(1m, 0m, 0m) { }
}
