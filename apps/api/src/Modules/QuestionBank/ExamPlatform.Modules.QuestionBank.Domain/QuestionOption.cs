using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>One answer option of a <see cref="Question"/>.</summary>
public sealed class QuestionOption : Entity
{
    /// <summary>The question this option belongs to.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>The text shown to the candidate.</summary>
    public string Text { get; private set; }

    /// <summary>Whether choosing this option is the right answer.</summary>
    public bool IsCorrect { get; private set; }

    /// <summary>Position among the question's options, from 1.</summary>
    public int Order { get; private set; }

    // For EF Core.
    private QuestionOption() : base(Guid.Empty) => Text = null!;

    internal QuestionOption(Guid questionId, string text, bool isCorrect, int order) : base(Guid.NewGuid())
    {
        QuestionId = questionId;
        Text = text;
        IsCorrect = isCorrect;
        Order = order;
    }
}
