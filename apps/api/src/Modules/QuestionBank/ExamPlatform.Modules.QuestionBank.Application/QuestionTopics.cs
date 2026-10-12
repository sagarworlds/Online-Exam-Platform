using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The <see cref="IQuestionTopics"/> the candidate analytics read a question's topics through (FR-36).</summary>
public sealed class QuestionTopics(IQuestionRepository repository) : IQuestionTopics
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetTopicsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        var topics = new Dictionary<Guid, IReadOnlyList<string>>();
        if (questionIds.Count == 0)
            return topics;

        foreach (var question in await repository.GetManyAsync(questionIds, cancellationToken))
        {
            // A question with no labels says nothing about any topic, so it is left out rather than filed under an empty name.
            if (question.Topics.Length > 0)
                topics[question.Id] = question.Topics;
        }

        return topics;
    }
}
