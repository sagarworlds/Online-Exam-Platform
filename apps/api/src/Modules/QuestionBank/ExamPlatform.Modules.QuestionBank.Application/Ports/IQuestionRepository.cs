using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>Persistence port for <see cref="Question"/>.</summary>
public interface IQuestionRepository
{
    /// <summary>Starts tracking a new question; it is stored when the unit of work saves.</summary>
    /// <param name="question">The question to add.</param>
    void Add(Question question);

    /// <summary>Loads one question with its options.</summary>
    /// <param name="questionId">The question's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The question, or <see langword="null"/> when none has that id.</returns>
    Task<Question?> GetByIdAsync(Guid questionId, CancellationToken cancellationToken);

    /// <summary>Loads several questions with their options in one query.</summary>
    /// <param name="questionIds">The ids to load; unknown ids are skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Question>> GetManyAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);

    /// <summary>Lists the most recently created questions, newest first.</summary>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Question>> ListNewestAsync(int take, CancellationToken cancellationToken);
}
