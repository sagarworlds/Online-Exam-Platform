using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// One question of the paper drawn for an <see cref="Attempt"/>, with the section it sits in. Only attempts at exams that draw
/// questions for each candidate have a paper; for the rest the exam's own fixed list is the paper.
/// </summary>
/// <remarks>
/// The paper is stored rather than recomputed because the draw is random: the candidate must see the same questions when they
/// reload or resume, and the scorer and the review must mark exactly what was shown.
/// </remarks>
public sealed class AttemptQuestion : Entity
{
    /// <summary>The attempt whose paper this is part of.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The exam section the question sits in.</summary>
    public Guid SectionId { get; private set; }

    /// <summary>Position within the section, from 1, before any shuffling.</summary>
    public int Order { get; private set; }

    /// <summary>The question-bank id of the question.</summary>
    public Guid QuestionId { get; private set; }

    // For EF Core.
    private AttemptQuestion() : base(Guid.Empty)
    {
    }

    internal AttemptQuestion(Guid attemptId, Guid sectionId, int order, Guid questionId) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        SectionId = sectionId;
        Order = order;
        QuestionId = questionId;
    }
}
